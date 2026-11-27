using System;
using System.Collections.Generic;
using Madlands.Shared.Game;
using Madlands.Shared.Net;

namespace Madlands.ClientCore
{
    /// <summary>
    /// The client's GM command service (docs/GMMenu). The developer menu and automation (the Playtest Controller)
    /// call <see cref="Execute"/> with the same stable ids and arguments and get the same structured results.
    /// It validates against the shared schema before sending, refuses a second request for a command that is
    /// still waiting, times requests out, keeps a short history and runs client-side commands (overlays)
    /// through handlers the game registers. The server checks rights and arguments again: nothing here grants
    /// anything.
    /// </summary>
    public sealed class GmCommandClient
    {
        public const int HistorySize = 12;
        public const long TimeoutMs = 5000;

        /// <summary>A client-side command: gets validated arguments, returns its result.</summary>
        public delegate GmCommandResult LocalHandler(GmArgValues args);

        private sealed class Pending
        {
            public string CommandId;
            public long SentAtMs;
            public Action<GmCommandResult> Done;
        }

        private readonly GameClient _client;
        private readonly Dictionary<string, LocalHandler> _local = new Dictionary<string, LocalHandler>(StringComparer.Ordinal);
        private readonly Dictionary<uint, Pending> _pending = new Dictionary<uint, Pending>();
        private readonly List<GmCommandResult> _history = new List<GmCommandResult>();
        private readonly List<uint> _expired = new List<uint>();
        private uint _lastRequestId;
        private long _nowMs;

        internal GmCommandClient(GameClient client)
        {
            _client = client;
            client.GmResultReceived += OnResult;
            client.EnteredWorld += _ => FailAllPending("You left the world before the answer came.");
            client.Disconnected += _ => FailAllPending("The connection closed before the answer came.");
        }

        /// <summary>Every finished command (server answers, client-side commands and refusals), newest last.</summary>
        public event Action<GmCommandResult> Completed;

        /// <summary>True when the server says this account is a GM and the player is in the world.</summary>
        public bool IsAvailable => _client.State == ClientState.InWorld && _client.GmStatus.GmLevel > 0;

        public IReadOnlyList<GmCommandResult> History => _history;

        public bool IsPending(string commandId)
        {
            foreach (var p in _pending.Values)
                if (p.CommandId == commandId) return true;
            return false;
        }

        /// <summary>Lets the game run a client-side command (<see cref="GmSide.Client"/>), e.g. an overlay toggle.</summary>
        public void RegisterLocal(string commandId, LocalHandler handler)
        {
            var info = GmCommands.Find(commandId);
            if (info == null || info.Side != GmSide.Client) throw new ArgumentException("Not a client-side GM command: " + commandId);
            _local[commandId] = handler;
        }

        public void UnregisterLocal(string commandId) => _local.Remove(commandId);

        /// <summary>Convenience for one argument.</summary>
        public uint Execute(string commandId, string key, string value, Action<GmCommandResult> done = null) =>
            Execute(commandId, new[] { new KeyValuePair<string, string>(key, value) }, done);

        /// <summary>
        /// Runs a command by id. Returns the request id (0 if it finished at once). <paramref name="done"/> and
        /// <see cref="Completed"/> get the result: at once for refusals and client-side commands, else when the
        /// server answers or after <see cref="TimeoutMs"/>.
        /// </summary>
        public uint Execute(string commandId, IReadOnlyList<KeyValuePair<string, string>> args = null, Action<GmCommandResult> done = null)
        {
            var info = GmCommands.Find(commandId);
            if (info == null) return Finish(Local(commandId, GmResultCode.UnknownCommand, "Unknown GM command '" + commandId + "'."), done);
            if (_client.State != ClientState.InWorld) return Finish(Local(commandId, GmResultCode.NotAvailable, "You are not in the world."), done);
            if (_client.GmStatus.GmLevel < info.MinGmLevel)
                return Finish(Local(commandId, GmResultCode.Unauthorized, "You are not allowed to use GM commands."), done);
            string error = GmCommands.Parse(info, args, out var values);
            if (error != null) return Finish(Local(commandId, GmResultCode.InvalidArgument, error), done);

            if (info.Side == GmSide.Client)
            {
                if (!_local.TryGetValue(commandId, out var handler))
                    return Finish(Local(commandId, GmResultCode.NotAvailable, commandId + " is not supported by this client."), done);
                GmCommandResult result;
                try
                {
                    result = handler(values) ?? Local(commandId, GmResultCode.ExecutionFailed, "No result.");
                }
                catch (Exception ex)
                {
                    result = Local(commandId, GmResultCode.ExecutionFailed, commandId + " failed: " + ex.Message);
                }
                result.CommandId = commandId;
                return Finish(result, done);
            }

            if (IsPending(commandId)) return Finish(Local(commandId, GmResultCode.Busy, commandId + " is still waiting for the server."), done);
            uint id = ++_lastRequestId;
            if (id == 0) id = ++_lastRequestId;
            _pending[id] = new Pending { CommandId = commandId, SentAtMs = _nowMs, Done = done };
            _client.SendGmCommand(id, commandId, args);
            return id;
        }

        /// <summary>A result made in the client (refusals and client-side commands).</summary>
        public static GmCommandResult Local(string commandId, GmResultCode code, string message)
        {
            return new GmCommandResult { CommandId = commandId, Code = code, Message = message };
        }

        /// <summary>Called by <see cref="GameClient.Update"/>: times out requests the server never answered.</summary>
        internal void Update(long nowMs)
        {
            _nowMs = nowMs;
            if (_pending.Count == 0) return;
            _expired.Clear();
            foreach (var pair in _pending)
                if (nowMs - pair.Value.SentAtMs > TimeoutMs) _expired.Add(pair.Key);
            foreach (var id in _expired)
            {
                var p = _pending[id];
                _pending.Remove(id);
                var result = Local(p.CommandId, GmResultCode.Timeout, "No answer from the server.");
                result.RequestId = id;
                Finish(result, p.Done);
            }
        }

        private void OnResult(GmCommandResult message)
        {
            // Copy: the message object is reused for the next packet.
            var result = new GmCommandResult
            {
                RequestId = message.RequestId,
                CommandId = message.CommandId,
                Code = message.Code,
                Message = message.Message,
            };
            result.Data.AddRange(message.Data);
            Action<GmCommandResult> done = null;
            if (_pending.TryGetValue(message.RequestId, out var p))
            {
                _pending.Remove(message.RequestId);
                done = p.Done;
            }
            Finish(result, done);
        }

        private void FailAllPending(string why)
        {
            if (_pending.Count == 0) return;
            var ids = new List<uint>(_pending.Keys);
            foreach (var id in ids)
            {
                var p = _pending[id];
                _pending.Remove(id);
                var result = Local(p.CommandId, GmResultCode.NotAvailable, why);
                result.RequestId = id;
                Finish(result, p.Done);
            }
        }

        private uint Finish(GmCommandResult result, Action<GmCommandResult> done)
        {
            _history.Add(result);
            if (_history.Count > HistorySize) _history.RemoveAt(0);
            done?.Invoke(result);
            Completed?.Invoke(result);
            return result.RequestId;
        }
    }
}

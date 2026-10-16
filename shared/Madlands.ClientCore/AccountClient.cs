using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Madlands.Shared;
using Madlands.Shared.Net;

namespace Madlands.ClientCore
{
    /// <summary>
    /// Talks to the account API (login, characters) over HTTP with binary message bodies.
    /// Never throws for expected failures: every call returns a response whose
    /// <see cref="ApiResponse.Result"/> says what happened (including <see cref="ApiResultCode.Unreachable"/>).
    /// Methods are async; in Unity, awaiting them from the main thread resumes on the main thread.
    /// </summary>
    public sealed class AccountClient : IDisposable
    {
        public const int DefaultHttpPort = 7780;
        private const int MaxResponseBytes = 64 * 1024;

        private static readonly MediaTypeHeaderValue BinaryContent = new MediaTypeHeaderValue("application/octet-stream");

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _baseUrl;

        /// <param name="baseUrl">e.g. http://127.0.0.1:7780 (production: https behind a TLS proxy).</param>
        /// <param name="http">Optional shared HttpClient (bots share one); otherwise one is created and owned.</param>
        public AccountClient(string baseUrl, HttpClient http = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _ownsHttp = http == null;
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public string BaseUrl => _baseUrl;
        public string SessionToken { get; private set; }
        public AuthResponse LastAuth { get; private set; }

        /// <summary>"host" or "host:port" (port = HTTP port) to an http:// base URL.</summary>
        public static string BaseUrlFor(string server)
        {
            if (string.IsNullOrWhiteSpace(server)) server = "127.0.0.1";
            server = server.Trim();
            if (server.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || server.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return server;
            return server.Contains(":") ? "http://" + server : "http://" + server + ":" + DefaultHttpPort;
        }

        public async Task<AuthResponse> RegisterAsync(string username, string password)
        {
            var response = await PostAsync<AuthResponse>("register", new RegisterRequest { Username = username, Password = password }).ConfigureAwait(false);
            Remember(response);
            return response;
        }

        public async Task<AuthResponse> LoginAsync(string username, string password)
        {
            var response = await PostAsync<AuthResponse>("login", new LoginRequest { Username = username, Password = password }).ConfigureAwait(false);
            Remember(response);
            return response;
        }

        public Task<CharacterListResponse> GetCharactersAsync() =>
            PostAsync<CharacterListResponse>("characters/list", new CharacterListRequest());

        public Task<CreateCharacterResponse> CreateCharacterAsync(string name, byte race, byte characterClass) =>
            PostAsync<CreateCharacterResponse>("characters/create", new CreateCharacterRequest { Name = name, Race = race, Class = characterClass });

        public Task<ApiResult> DeleteCharacterAsync(ulong characterId) =>
            PostAsync<ApiResult>("characters/delete", new DeleteCharacterRequest { CharacterId = characterId });

        public async Task<ApiResult> LogoutAsync()
        {
            var result = await PostAsync<ApiResult>("logout", new LogoutRequest()).ConfigureAwait(false);
            SessionToken = null;
            LastAuth = null;
            return result;
        }

        /// <summary>Convenience for dev tools and bots: log in, registering the account first if it does not exist.</summary>
        public async Task<AuthResponse> LoginOrRegisterAsync(string username, string password)
        {
            var login = await LoginAsync(username, password).ConfigureAwait(false);
            if (login.Result != ApiResultCode.InvalidCredentials) return login;
            var register = await RegisterAsync(username, password).ConfigureAwait(false);
            return register.Result == ApiResultCode.UsernameTaken ? login : register;
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        private void Remember(AuthResponse response)
        {
            if (!response.Ok) return;
            SessionToken = response.SessionToken;
            LastAuth = response;
        }

        private async Task<T> PostAsync<T>(string path, IMessage request) where T : ApiResponse, new()
        {
            var writer = new PacketWriter(128);
            Packet.Encode(writer, request);
            var message = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/api/v1/" + path)
            {
                Content = new ByteArrayContent(writer.ToArray()),
            };
            message.Content.Headers.ContentType = BinaryContent;
            message.Headers.Add(ProtocolVersion.HttpHeader, ProtocolVersion.Current.ToString());
            if (SessionToken != null) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SessionToken);

            byte[] body;
            try
            {
                using (var response = await _http.SendAsync(message).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        return Fail<T>(ApiResultCode.ServerError, "Server answered HTTP " + (int)response.StatusCode + ".");
                    body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException)
            {
                return Fail<T>(ApiResultCode.Unreachable, "Could not reach the server at " + _baseUrl + ".");
            }
            finally
            {
                message.Dispose();
            }

            if (body.Length > MaxResponseBytes) return Fail<T>(ApiResultCode.ServerError, "Response too large.");
            return Decode<T>(body);
        }

        private static T Decode<T>(byte[] body) where T : ApiResponse, new()
        {
            try
            {
                var reader = new PacketReader(body);
                var opcode = Packet.ReadOpcode(ref reader);
                var expected = new T();
                if (opcode == expected.Opcode)
                {
                    expected.Read(ref reader);
                    return expected;
                }
                if (opcode == Opcode.ApiResult)
                {
                    var error = new ApiResult();
                    error.Read(ref reader);
                    return Fail<T>(error.Result, error.Message);
                }
                return Fail<T>(ApiResultCode.ServerError, "Unexpected response " + opcode + ".");
            }
            catch (PacketException ex)
            {
                return Fail<T>(ApiResultCode.ServerError, "Malformed response: " + ex.Message);
            }
        }

        private static T Fail<T>(ApiResultCode code, string message) where T : ApiResponse, new() =>
            new T { Result = code, Message = message };
    }
}

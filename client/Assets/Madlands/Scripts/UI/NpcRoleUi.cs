using Madlands.Shared.Game;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// How an <see cref="NpcRole"/> looks: its symbol over the head and on the maps (Art/Icons, made by
    /// art/blender/icons.py) and its title under the name.
    /// </summary>
    public static class NpcRoleUi
    {
        public static Texture2D Icon(NpcRole role)
        {
            string name;
            switch (role)
            {
                case NpcRole.GeneralGoods: name = "IconGoods"; break;
                case NpcRole.Innkeeper: name = "IconInn"; break;
                case NpcRole.Weaponsmith: name = "IconWeapons"; break;
                default: return null;
            }
            var catalog = ArtCatalog.Instance;
            return catalog != null ? catalog.GetIcon(name) : null;
        }

        /// <summary>"&lt;Innkeeper&gt;", or null for no role.</summary>
        public static string Title(NpcRole role)
        {
            switch (role)
            {
                case NpcRole.GeneralGoods: return Loc.T("nameplate.title", Loc.T("role.generalGoods"));
                case NpcRole.Innkeeper: return Loc.T("nameplate.title", Loc.T("role.innkeeper"));
                case NpcRole.Weaponsmith: return Loc.T("nameplate.title", Loc.T("role.weaponsmith"));
                default: return null;
            }
        }
    }
}

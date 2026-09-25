// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using ModLoader;
using UnityEngine;

namespace SFSDLSS5
{
    /// <summary>
    /// DLSS 5 for SFS - 管理模组
    /// 注入后端的游戏内管理与状态显示（Mods 文件夹形态）
    /// </summary>
    public class Main : Mod
    {
        internal static Main main;

        public Main()
        {
            main = this;
        }

        public override string ModNameID => "SFSDLSS5";

        public override string DisplayName => "DLSS 5 for SFS";

        public override string Author => "maozhongmao / yangchengtong";

        public override string MinimumGameVersionNecessary => "1.6.0.0";

        public override string ModVersion => "0.0.1";

        public override string Description =>
            "DLSS 5 neural rendering integration for SFS. Manages the injection backend and provides in-game status and controls.";

        public override void Early_Load()
        {
            Debug.Log("[SFSDLSS5] Early_Load v" + ModVersion);
        }

        public override void Load()
        {
            var go = new GameObject("SFSDLSS5_Runtime");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runtime>();
            Debug.Log("[SFSDLSS5] Load complete - press F9 in game for the control panel");
        }
    }
}

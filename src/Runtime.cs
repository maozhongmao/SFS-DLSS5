// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SFSDLSS5
{
    /// <summary>
    /// 常驻运行时：状态检测 + DLSS 5 开关控制（F9 打开控制面板）
    /// </summary>
    public class Runtime : MonoBehaviour
    {
        public static Runtime main;

        private bool showWindow;
        private string statusLine = "(checking...)";
        private string feedTail = "";
        private float nextCheck;
        private bool paused;

        /// <summary>游戏根目录（Spaceflight Simulator Game）</summary>
        private static string GameDir => Path.GetDirectoryName(Application.dataPath);

        /// <summary>注入后端的关键文件清单</summary>
        private static readonly string[] InjectFiles =
        {
            "dxgi.dll",
            "dlss5-feed.addon64",
            "renodx-dlss5.addon64",
            "nvngx_dlssnr.dll",
            "nvngx_dlss.dll"
        };

        private void Awake()
        {
            main = this;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
                showWindow = !showWindow;

            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + 5f;
                RefreshStatus();
            }
        }

        private void RefreshStatus()
        {
            try
            {
                string dir = GameDir;
                int found = 0;
                foreach (string f in InjectFiles)
                    if (File.Exists(Path.Combine(dir, f))) found++;

                statusLine = found == InjectFiles.Length
                    ? "Backend: INSTALLED (all files present)"
                    : "Backend: INCOMPLETE (" + found + "/" + InjectFiles.Length + " files)";

                string cfgPath = Path.Combine(dir, "dlss5-feed.cfg");
                if (File.Exists(cfgPath))
                {
                    string cfg = File.ReadAllText(cfgPath);
                    paused = Regex.IsMatch(cfg, @"enabled=0");
                    statusLine += paused ? "  |  NR: PAUSED" : "  |  NR: ACTIVE";
                }
                else
                {
                    statusLine += "  |  dlss5-feed.cfg not found";
                }

                string logPath = Path.Combine(dir, "dlss5-feed.log");
                if (File.Exists(logPath))
                {
                    string[] lines = File.ReadAllLines(logPath);
                    int n = lines.Length;
                    var sb = new StringBuilder();
                    for (int i = Math.Max(0, n - 8); i < n; i++)
                        sb.AppendLine(lines[i]);
                    feedTail = sb.ToString();
                }
                else
                {
                    feedTail = "(dlss5-feed.log not found)";
                }
            }
            catch (Exception e)
            {
                statusLine = "check error: " + e.Message;
            }
        }

        private void SetPaused(bool value)
        {
            try
            {
                string cfgPath = Path.Combine(GameDir, "dlss5-feed.cfg");
                if (!File.Exists(cfgPath)) return;

                string cfg = File.ReadAllText(cfgPath);
                cfg = Regex.Replace(cfg, @"enabled=\d", "enabled=" + (value ? "0" : "1"));
                File.WriteAllText(cfgPath, cfg);
                paused = value;
                nextCheck = 0f; // 立即刷新
                Debug.Log("[SFSDLSS5] DLSS 5 " + (value ? "paused" : "resumed"));
            }
            catch (Exception e)
            {
                Debug.LogError("[SFSDLSS5] " + e);
            }
        }

        private void OnGUI()
        {
            if (!showWindow) return;

            GUI.skin.label.fontSize = 12;
            GUI.Window(0x5F51, new Rect(24f, 24f, 480f, 320f), DrawWindow, "DLSS 5 for SFS  v0.0.1   (F9)");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(4f);
            GUILayout.Label("Backend dir: " + GameDir);
            GUILayout.Label(statusLine);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(paused ? "Resume DLSS 5" : "Pause DLSS 5", GUILayout.Width(170f)))
                SetPaused(!paused);
            if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
                nextCheck = 0f;
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("dlss5-feed.log (tail):");
            GUILayout.TextArea(feedTail, GUILayout.ExpandHeight(true));

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }
    }
}

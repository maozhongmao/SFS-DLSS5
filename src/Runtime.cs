// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using UnityEngine;

namespace SFSDLSS5
{
    /// <summary>
    /// 常驻运行时：状态检测 + DLSS 5 开关控制 + 后端组件下载/解密解压/部署（F9 打开控制面板）
    /// 解压能力（SharpCompress）已内嵌进本 DLL，无外部前置依赖。
    /// </summary>
    public class Runtime : MonoBehaviour
    {
        public static Runtime main;

        private bool showWindow;
        private string statusLine = "(checking...)";
        private string feedTail = "";
        private float nextCheck;
        private bool paused;

        // ── 下载/解压状态（后台线程写，UI 线程读） ──
        private volatile bool working;
        private volatile string workText = "";
        private volatile float workProgress;
        private volatile string lastError = "";

        /// <summary>加密包密码（内嵌，无需用户输入）</summary>
        private const string ArchivePassword = "X9WM9w10XPdGQVaGv4MrKyQ";

        /// <summary>游戏根目录（Spaceflight Simulator Game）</summary>
        private static string GameDir => Path.GetDirectoryName(Application.dataPath);

        /// <summary>模组目录（Mods/SFSDLSS5）</summary>
        private static string ModDir => Path.Combine(GameDir, "Mods", "SFSDLSS5");

        /// <summary>下载缓存目录</summary>
        private static string CacheDir => Path.Combine(ModDir, "cache");

        /// <summary>解压产物目录</summary>
        private static string ExtractDir => Path.Combine(CacheDir, "extracted");

        /// <summary>下载源配置文件（一行 URL）</summary>
        private static string SourceFile => Path.Combine(ModDir, "download-source.txt");

        /// <summary>注入后端的关键文件清单（状态检测用）</summary>
        private static readonly string[] InjectFiles =
        {
            "dxgi.dll",
            "dlss5-feed.addon64",
            "renodx-dlss5.addon64",
            "nvngx_dlssnr.dll",
            "nvngx_dlss.dll"
        };

        /// <summary>需要下载的加密包（顺序即拼接顺序）</summary>
        private static readonly string[] Packages =
        {
            "dlss5-part1.7z.001",
            "dlss5-part1.7z.002",
            "dlss5-part2.7z"
        };

        /// <summary>解压后需要部署到游戏根目录的文件</summary>
        private static readonly string[] BackendComponents =
        {
            "nvngx_dlssnr.dll",
            "nvngx_dlss.dll",
            "dxgi.dll",
            "dlss5-feed.addon64",
            "renodx-dlss5.addon64",
            "dlss5oneclick.exe"
        };

        private void Awake()
        {
            main = this;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
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
                nextCheck = 0f;
                Debug.Log("[SFSDLSS5] DLSS 5 " + (value ? "paused" : "resumed"));
            }
            catch (Exception e)
            {
                Debug.LogError("[SFSDLSS5] " + e);
            }
        }

        // ── 下载引擎 ──

        private string DownloadSource()
        {
            try
            {
                if (File.Exists(SourceFile))
                {
                    string s = File.ReadAllText(SourceFile).Trim();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return "https://api.stch.de5.net/dlss5";
        }

        /// <summary>下载全部加密包并解压到缓存（后台线程执行）</summary>
        private void DownloadAndExtract()
        {
            if (working) return;

            string src = DownloadSource();
            working = true;
            lastError = "";
            workProgress = 0f;
            workText = "准备中...";

            Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(CacheDir);

                    // ── 1. 下载 3 个加密包 ──
                    int total = Packages.Length;
                    for (int i = 0; i < total; i++)
                    {
                        string f = Packages[i];
                        string url = src.TrimEnd('/') + "/" + f;
                        string dst = Path.Combine(CacheDir, f);

                        if (File.Exists(dst) && new FileInfo(dst).Length > 0)
                        {
                            workProgress = (i + 1) / (float)(total + 1);
                            continue;
                        }

                        workText = string.Format("下载 {0} ({1}/{2})", f, i + 1, total);

                        using (var wc = new WebClient())
                        {
                            int idx = i;
                            wc.DownloadProgressChanged += (s, e) =>
                            {
                                workProgress = (idx + e.ProgressPercentage / 100f) / (total + 1);
                            };

                            var done = new ManualResetEventSlim(false);
                            Exception err = null;
                            wc.DownloadFileCompleted += (s, e) =>
                            {
                                err = e.Error;
                                done.Set();
                            };

                            wc.DownloadFileAsync(new Uri(url), dst);
                            done.Wait();

                            if (err != null)
                            {
                                try { if (File.Exists(dst)) File.Delete(dst); } catch { }
                                throw new Exception(f + " 下载失败：" + err.Message);
                            }
                        }

                        workProgress = (i + 1) / (float)(total + 1);
                    }

                    // ── 2. 解密解压 ──
                    workText = "解压中...";
                    Directory.CreateDirectory(ExtractDir);

                    // part1 为两卷拆分，先拼接
                    string p1a = Path.Combine(CacheDir, "dlss5-part1.7z.001");
                    string p1b = Path.Combine(CacheDir, "dlss5-part1.7z.002");
                    string merged = Path.Combine(CacheDir, "dlss5-part1.7z");
                    if (!File.Exists(merged))
                        ConcatFiles(new[] { p1a, p1b }, merged);
                    Extract7z(merged, ExtractDir);
                    workProgress = (total + 0.6f) / (total + 1);

                    Extract7z(Path.Combine(CacheDir, "dlss5-part2.7z"), ExtractDir);
                    workProgress = 1f;

                    int n = 0;
                    foreach (string f in BackendComponents)
                        if (File.Exists(Path.Combine(ExtractDir, f))) n++;

                    workText = string.Format("✓ 已解压 {0}/{1} 个组件到缓存", n, BackendComponents.Length);
                }
                catch (Exception e)
                {
                    lastError = e.Message;
                    workText = "✗ 中断";
                }
                finally
                {
                    working = false;
                }
            });
        }

        /// <summary>按顺序拼接文件（分卷合并）</summary>
        private static void ConcatFiles(string[] parts, string output)
        {
            using (var outStream = File.Create(output))
            {
                foreach (string p in parts)
                {
                    using (var inStream = File.OpenRead(p))
                        inStream.CopyTo(outStream);
                }
            }
        }

        /// <summary>用内嵌的 SharpCompress 解压 7z（AES-256 加密内容）</summary>
        private static void Extract7z(string archivePath, string destDir)
        {
            var options = new ReaderOptions { Password = ArchivePassword };
            using (var archive = ArchiveFactory.OpenArchive(archivePath, options))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    entry.WriteToDirectory(destDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }
        }

        // ── 部署（解压产物 → 游戏根目录） ──

        private void DeployFromCache()
        {
            try
            {
                if (!Directory.Exists(ExtractDir))
                {
                    lastError = "还没解压，请先点 Download + Extract";
                    return;
                }

                int n = 0;
                foreach (string f in BackendComponents)
                {
                    string src = Path.Combine(ExtractDir, f);
                    if (!File.Exists(src)) continue;

                    string dst = Path.Combine(GameDir, f);
                    try
                    {
                        File.Copy(src, dst, true);
                        n++;
                    }
                    catch (IOException)
                    {
                        lastError = "无法写入 " + f + "（游戏正在运行？请关闭游戏后重试）";
                        return;
                    }
                }

                // 保留一份部署工具，便于卸载
                string tool = Path.Combine(ExtractDir, "dlss5oneclick.exe");
                if (File.Exists(tool))
                {
                    try { File.Copy(tool, Path.Combine(ModDir, "dlss5oneclick.exe"), true); } catch { }
                }

                nextCheck = 0f;
                workText = "✓ 已部署 " + n + " 个文件，重启游戏生效";
            }
            catch (Exception e)
            {
                lastError = e.Message;
            }
        }

        // ── UI ──

        private void OnGUI()
        {
            if (!showWindow) return;

            GUI.skin.label.fontSize = 12;
            GUI.Window(0x5F51, new Rect(24f, 24f, 560f, 400f), DrawWindow, "DLSS 5 for SFS  v0.0.3   (F9)");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(4f);
            GUILayout.Label("Backend dir: " + GameDir);
            GUILayout.Label(statusLine);
            GUILayout.Space(6f);

            // 控制行
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(paused ? "Resume DLSS 5" : "Pause DLSS 5", GUILayout.Width(140f)))
                SetPaused(!paused);
            if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
                nextCheck = 0f;
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            // 下载/解压/部署区
            GUILayout.Label("后端组件（下载加密包 → 内嵌解压 → 部署到游戏目录）");
            GUILayout.BeginHorizontal();
            GUI.enabled = !working;
            if (GUILayout.Button(working ? "处理中..." : "Download + Extract", GUILayout.Width(170f)))
                DownloadAndExtract();
            GUI.enabled = true;
            if (GUILayout.Button("Deploy to game", GUILayout.Width(140f)))
                DeployFromCache();
            GUILayout.EndHorizontal();

            if (working || workProgress > 0f)
            {
                GUILayout.Space(4f);
                Rect bar = GUILayoutUtility.GetRect(1f, 18f);
                GUI.Box(bar, "");
                var fill = new Rect(bar.x + 1f, bar.y + 1f, (bar.width - 2f) * Mathf.Clamp01(workProgress), bar.height - 2f);
                GUI.Box(fill, "");
                GUI.Label(new Rect(bar.x + 6f, bar.y + 1f, bar.width, bar.height), workText);
            }
            else if (!string.IsNullOrEmpty(workText))
            {
                GUILayout.Label(workText);
            }

            if (!string.IsNullOrEmpty(lastError))
            {
                GUILayout.Space(4f);
                GUILayout.Label("⚠ " + lastError);
            }

            GUILayout.Space(8f);
            GUILayout.Label("dlss5-feed.log (tail):");
            GUILayout.TextArea(feedTail, GUILayout.ExpandHeight(true));

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }
    }
}

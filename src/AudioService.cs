using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KaoYanReminder
{
    public class VoiceOption
    {
        public string Description;
        public bool IsChinese;
        public override string ToString() { return Description; }
    }

    /// <summary>
    /// 声音服务：
    ///  - wav 用 SoundPlayer（支持循环）
    ///  - mp3 等用 Windows 自带 MCI（支持循环、中文路径）
    ///  - 语音播报用系统 SAPI（中文 Huihui 等）
    /// </summary>
    public class AudioService : IDisposable
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, StringBuilder returnValue, int returnLength, IntPtr hwnd);

        private readonly Action<string> log;
        private readonly Timer stopTimer;
        private SoundPlayer wavPlayer;
        private string mciAlias;
        private bool mciOpen;
        private bool disposed;
        private int aliasCounter;
        private object sapi;
        private bool sapiTried;

        public string LastError { get; private set; }

        public AudioService(Action<string> logger)
        {
            log = logger;
            stopTimer = new Timer();
            stopTimer.Interval = 30000;
            stopTimer.Tick += OnStopTimer;
        }

        public static string DefaultAlarmPath
        {
            get
            {
                string[] candidates = new string[]
                {
                    @"C:\Windows\Media\Alarm01.wav",
                    @"C:\Windows\Media\Alarm02.wav",
                    @"C:\Windows\Media\Windows Notify System Generic.wav",
                    @"C:\Windows\Media\notify.wav"
                };
                foreach (string c in candidates)
                {
                    try { if (File.Exists(c)) return c; } catch { }
                }
                return null;
            }
        }

        public static string ResolveSoundFile(string configured)
        {
            try
            {
                if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
            }
            catch { }
            return DefaultAlarmPath;
        }

        public static bool IsSupportedExtension(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".wav" || ext == ".mp3" || ext == ".wma";
        }

        /// <summary>播放提醒铃声；loop=true 时循环，超过 maxSeconds 自动停止。</summary>
        public bool PlayRingtone(string configuredFile, bool loop, int maxSeconds, bool mute)
        {
            StopAudio();
            if (mute) return false;
            string file = ResolveSoundFile(configuredFile);
            if (file == null) return FallbackBeep();
            if (file.ToLowerInvariant().EndsWith(".wav")) return PlayWav(file, loop, maxSeconds);
            return PlayMci(file, loop, maxSeconds);
        }

        public bool PlayOnce(string configuredFile, int maxSeconds, bool mute)
        {
            return PlayRingtone(configuredFile, false, maxSeconds, mute);
        }

        public bool Preview(string configuredFile)
        {
            return PlayRingtone(configuredFile, false, 4, false);
        }

        private bool PlayWav(string file, bool loop, int maxSeconds)
        {
            try
            {
                wavPlayer = new SoundPlayer(file);
                if (loop) wavPlayer.PlayLooping();
                else wavPlayer.Play();
                if (maxSeconds > 0) ScheduleStop(maxSeconds);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                SafeLog("[声音] 播放 wav 失败：" + ex.Message);
                CloseWav();
                return FallbackBeep();
            }
        }

        private bool PlayMci(string file, bool loop, int maxSeconds)
        {
            try
            {
                string alias = "kyr" + (++aliasCounter);
                StringBuilder sb = new StringBuilder(512);
                int rc = Mci("open \"" + file + "\" type mpegvideo alias " + alias, sb);
                if (rc != 0) rc = Mci("open \"" + file + "\" alias " + alias, sb);
                if (rc != 0)
                {
                    LastError = "MCI open rc=" + rc;
                    SafeLog("[声音] 无法打开音频（" + rc + "）：" + file);
                    return FallbackBeep();
                }
                mciAlias = alias;
                mciOpen = true;
                rc = Mci("play " + alias + (loop ? " repeat" : ""), sb);
                if (rc != 0)
                {
                    LastError = "MCI play rc=" + rc;
                    CloseMci();
                    return FallbackBeep();
                }
                if (maxSeconds > 0) ScheduleStop(maxSeconds);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                SafeLog("[声音] MCI 异常：" + ex.Message);
                CloseMci();
                return FallbackBeep();
            }
        }

        private bool FallbackBeep()
        {
            try
            {
                string def = DefaultAlarmPath;
                if (def != null)
                {
                    wavPlayer = new SoundPlayer(def);
                    wavPlayer.Play();
                    return true;
                }
            }
            catch { }
            try
            {
                SystemSounds.Exclamation.Play();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        private void ScheduleStop(int seconds)
        {
            stopTimer.Interval = Math.Max(1, seconds) * 1000;
            stopTimer.Stop();
            stopTimer.Start();
        }

        private void OnStopTimer(object sender, EventArgs e)
        {
            stopTimer.Stop();
            StopAudio();
        }

        public void StopAudio()
        {
            stopTimer.Stop();
            CloseMci();
            CloseWav();
        }

        private void CloseWav()
        {
            if (wavPlayer != null)
            {
                try { wavPlayer.Stop(); } catch { }
                try { wavPlayer.Dispose(); } catch { }
                wavPlayer = null;
            }
        }

        private void CloseMci()
        {
            if (mciOpen && !string.IsNullOrEmpty(mciAlias))
            {
                try
                {
                    StringBuilder sb = new StringBuilder(64);
                    Mci("stop " + mciAlias, sb);
                    Mci("close " + mciAlias, sb);
                }
                catch { }
            }
            mciOpen = false;
            mciAlias = null;
        }

        private int Mci(string command, StringBuilder result)
        {
            return mciSendString(command, result, result == null ? 0 : result.Capacity, IntPtr.Zero);
        }

        // ---------------- 语音播报 ----------------

        private bool EnsureSapi()
        {
            if (sapiTried) return sapi != null;
            sapiTried = true;
            try
            {
                Type t = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (t == null)
                {
                    SafeLog("[语音] 系统未安装 SAPI 语音组件");
                    return false;
                }
                sapi = Activator.CreateInstance(t);
                return sapi != null;
            }
            catch (Exception ex)
            {
                SafeLog("[语音] 初始化失败：" + ex.Message);
                LastError = ex.Message;
                sapi = null;
                return false;
            }
        }

        public List<VoiceOption> GetVoices()
        {
            List<VoiceOption> list = new List<VoiceOption>();
            try
            {
                if (!EnsureSapi()) return list;
                dynamic voice = sapi;
                foreach (dynamic token in voice.GetVoices())
                {
                    string desc = "";
                    try { desc = Convert.ToString(token.GetDescription()); } catch { }
                    if (string.IsNullOrEmpty(desc)) desc = "系统语音";
                    VoiceOption opt = new VoiceOption();
                    opt.Description = desc;
                    string lang = "";
                    try { lang = Convert.ToString(token.GetAttribute("Language")); } catch { }
                    opt.IsChinese = desc.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0
                        || desc.IndexOf("中文") >= 0
                        || lang == "804" || lang == "404" || lang == "1004";
                    list.Add(opt);
                }
            }
            catch (Exception ex)
            {
                SafeLog("[语音] 获取语音列表失败：" + ex.Message);
                LastError = ex.Message;
            }
            list.Sort(delegate(VoiceOption a, VoiceOption b)
            {
                if (a.IsChinese != b.IsChinese) return a.IsChinese ? -1 : 1;
                return string.Compare(a.Description, b.Description, StringComparison.Ordinal);
            });
            return list;
        }

        public bool Speak(string text, string voiceDescription, int volume, int rate)
        {
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                if (!EnsureSapi()) return false;
                dynamic voice = sapi;
                if (!string.IsNullOrEmpty(voiceDescription))
                {
                    try
                    {
                        foreach (dynamic token in voice.GetVoices())
                        {
                            string desc = "";
                            try { desc = Convert.ToString(token.GetDescription()); } catch { }
                            if (string.Equals(desc, voiceDescription, StringComparison.Ordinal))
                            {
                                voice.Voice = token;
                                break;
                            }
                        }
                    }
                    catch { }
                }
                if (volume < 0) volume = 0;
                if (volume > 100) volume = 100;
                if (rate < -10) rate = -10;
                if (rate > 10) rate = 10;
                voice.Volume = volume;
                voice.Rate = rate;
                voice.Speak(text, 1); // 1 = 异步
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                SafeLog("[语音] 播报失败：" + ex.Message);
                return false;
            }
        }

        public void StopSpeech()
        {
            try
            {
                if (sapi == null) return;
                dynamic voice = sapi;
                voice.Speak("", 3); // purge + async
            }
            catch { }
        }

        private void SafeLog(string message)
        {
            if (log != null)
            {
                try { log(message); } catch { }
            }
            else
            {
                Log.Write(message);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { StopAudio(); } catch { }
            try { StopSpeech(); } catch { }
            try { stopTimer.Dispose(); } catch { }
            if (sapi != null)
            {
                try { Marshal.ReleaseComObject(sapi); } catch { }
                sapi = null;
            }
        }
    }
}
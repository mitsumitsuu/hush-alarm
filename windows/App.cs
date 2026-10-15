using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HushAlarm {
    static class Program {
        [STAThread] static void Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>0 && args[0]=="--diagnose") { foreach(var h in AudioDevices.List()) Console.WriteLine(h.Name + " (headphone form " + h.FormFactor + ")"); return; }
            if(args.Length==2 && args[0]=="--preview") {
                using(var form=new AlarmForm(true)) { form.Show(); Application.DoEvents(); using(var bmp=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size)); bmp.Save(args[1]); } form.Hide(); } return;
            }
            bool created; using(var mutex=new System.Threading.Mutex(true,"Local\\HushAlarm-"+Environment.UserName,out created)) {
                if(!created) { MessageBox.Show("HUSH Alarmは起動済みです。","HUSH Alarm"); return; }
                try { using(var login=new LoginForm()) { if(login.ShowDialog()!=DialogResult.OK) return; } Application.Run(new AlarmForm(false)); }
                catch(Exception) { MessageBox.Show("起動できませんでした。もう一度お試しください。","HUSH Alarm"); }
            }
        }
    }
    static class LocalAuth {
        internal static string TestStoragePath = null;
        static string FilePath { get { return TestStoragePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HushAlarm","auth.dat"); } }
        public static bool Exists { get { return File.Exists(FilePath); } }
        public static void Create(string pin) {
            byte[] salt=new byte[24]; using(var rng=RandomNumberGenerator.Create()) rng.GetBytes(salt);
            byte[] hash; using(var derive=new Rfc2898DeriveBytes(pin,salt,150000)) hash=derive.GetBytes(32);
            byte[] data=new byte[68]; Buffer.BlockCopy(salt,0,data,0,24); Buffer.BlockCopy(hash,0,data,24,32); Save(data);
        }
        public static bool Verify(string pin,out int waitSeconds) {
            var data=ProtectedData.Unprotect(File.ReadAllBytes(FilePath),null,DataProtectionScope.CurrentUser);
            if(data.Length!=68) throw new InvalidDataException();
            long until=BitConverter.ToInt64(data,60); waitSeconds=(int)Math.Max(0,TimeSpan.FromTicks(Math.Max(0,until-DateTime.UtcNow.Ticks)).TotalSeconds);
            if(waitSeconds>0) return false;
            byte[] salt=new byte[24]; Buffer.BlockCopy(data,0,salt,0,24);
            byte[] hash; using(var derive=new Rfc2898DeriveBytes(pin,salt,150000)) hash=derive.GetBytes(32);
            int difference=0; for(int i=0;i<32;i++) difference|=hash[i]^data[24+i];
            int attempts=difference==0 ? 0 : BitConverter.ToInt32(data,56)+1;
            Buffer.BlockCopy(BitConverter.GetBytes(attempts),0,data,56,4);
            long next=attempts>=5 ? DateTime.UtcNow.AddSeconds(30).Ticks : 0;
            Buffer.BlockCopy(BitConverter.GetBytes(next),0,data,60,8); Save(data); return difference==0;
        }
        static void Save(byte[] data) {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllBytes(FilePath,ProtectedData.Protect(data,null,DataProtectionScope.CurrentUser));
        }
    }
    sealed class LoginForm : Form {
        TextBox pin=new TextBox(),confirm=new TextBox(); Label message=new Label(); bool setup;
        public LoginForm() {
            setup=!LocalAuth.Exists; Text="HUSH Alarm"; ClientSize=new Size(390,310); FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false; StartPosition=FormStartPosition.CenterScreen; Font=new Font("Yu Gothic UI",11);
            Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            var title=new Label {Text=setup ? "PINを作成" : "ログイン",Font=new Font(Font.FontFamily,22,FontStyle.Bold),AutoSize=true,Location=new Point(28,25)}; Controls.Add(title);
            Controls.Add(new Label {Text=setup ? "6〜12桁の数字" : "PIN",AutoSize=true,Location=new Point(30,83)});
            pin.SetBounds(30,112,330,32); pin.UseSystemPasswordChar=true; pin.MaxLength=12; Controls.Add(pin);
            confirm.SetBounds(30,154,330,32); confirm.UseSystemPasswordChar=true; confirm.MaxLength=12; confirm.Visible=setup; confirm.AccessibleName="PINの確認"; Controls.Add(confirm);
            var submit=new Button {Text=setup ? "作成する" : "ログイン",BackColor=Color.FromArgb(18,111,108),ForeColor=Color.White,FlatStyle=FlatStyle.Flat}; submit.SetBounds(30,205,330,42); submit.Click+=Submit; Controls.Add(submit); AcceptButton=submit;
            message.SetBounds(30,258,330,40); message.ForeColor=Color.DarkRed; Controls.Add(message); pin.AccessibleName="PIN";
        }
        void Submit(object sender,EventArgs e) {
            if(!Regex.IsMatch(pin.Text,"^[0-9]{6,12}$")) { message.Text="6〜12桁の数字を入力してください"; return; }
            try {
                if(setup) { if(pin.Text!=confirm.Text) {message.Text="PINが一致しません"; return;} LocalAuth.Create(pin.Text); }
                else { int wait; if(!LocalAuth.Verify(pin.Text,out wait)) { message.Text=wait>0 ? wait+"秒後にもう一度お試しください" : "PINを確認してください"; pin.Clear(); return; } }
                pin.Clear(); confirm.Clear(); DialogResult=DialogResult.OK; Close();
            } catch(Exception) { message.Text="認証情報を読み込めませんでした"; }
        }
    }
    sealed class AlarmForm : Form {
        ComboBox outputs=new ComboBox(),sounds=new ComboBox(); DateTimePicker clock=new DateTimePicker(); NumericUpDown minutes=new NumericUpDown();
        RadioButton timeMode=new RadioButton(),timerMode=new RadioButton(); TrackBar volume=new TrackBar();
        Label state=new Label(),remaining=new Label(),routeStatus=new Label(),volumeLabel=new Label();
        Button start=new Button(),stop=new Button(),snooze=new Button(),preview=new Button(),refresh=new Button();
        System.Windows.Forms.Timer tick=new System.Windows.Forms.Timer(); AlarmSession alarm=new AlarmSession(); HeadphonePlayer player=new HeadphonePlayer();
        Stopwatch monotonic=Stopwatch.StartNew(); int ticks; bool demo; DateTimeOffset previewUntil=DateTimeOffset.MinValue;
        Color accent=Color.FromArgb(18,111,108); string pinnedId;
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint state);
        public AlarmForm(bool demoMode) {
            demo=demoMode; Text="HUSH Alarm"; ClientSize=new Size(580,665); MinimumSize=new Size(570,695); StartPosition=FormStartPosition.CenterScreen; Font=new Font("Yu Gothic UI",11); BackColor=Color.FromArgb(246,248,247);
            Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=12,Padding=new Padding(28,22,28,22)};
            float[] heights={58,26,44,24,46,58,34,52,40,80,56,46}; foreach(var h in heights) root.RowStyles.Add(new RowStyle(SizeType.Absolute,h)); Controls.Add(root);
            root.Controls.Add(new Label {Text="HUSH",Font=new Font(Font.FontFamily,28,FontStyle.Bold),ForeColor=accent,Dock=DockStyle.Fill},0,0);
            root.Controls.Add(new Label {Text="イヤホン",Dock=DockStyle.Fill},0,1);
            var route=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2}; route.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); route.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,78));
            outputs.DropDownStyle=ComboBoxStyle.DropDownList; outputs.Dock=DockStyle.Fill; outputs.AccessibleName="出力先のイヤホン"; route.Controls.Add(outputs,0,0);
            refresh.Text="更新"; refresh.Dock=DockStyle.Fill; refresh.Click+=delegate { RefreshDevices(); }; route.Controls.Add(refresh,1,0); root.Controls.Add(route,0,2);
            routeStatus.Dock=DockStyle.Fill; routeStatus.ForeColor=accent; routeStatus.Font=new Font(Font.FontFamily,9); root.Controls.Add(routeStatus,0,3);
            var modes=new FlowLayoutPanel {Dock=DockStyle.Fill}; timeMode.Text="時刻指定"; timeMode.AutoSize=true; timeMode.Checked=true; timerMode.Text="仮眠タイマー"; timerMode.AutoSize=true; timeMode.CheckedChanged+=delegate {UpdateMode();}; modes.Controls.Add(timeMode); modes.Controls.Add(timerMode); root.Controls.Add(modes,0,4);
            var inputs=new FlowLayoutPanel {Dock=DockStyle.Fill}; clock.Format=DateTimePickerFormat.Custom; clock.CustomFormat="HH:mm"; clock.ShowUpDown=true; clock.Width=155; clock.Font=new Font(Font.FontFamily,23); clock.Value=DateTime.Now.AddMinutes(20); clock.AccessibleName="起きたい時刻";
            minutes.Minimum=1; minutes.Maximum=720; minutes.Value=20; minutes.Width=155; minutes.Font=clock.Font; minutes.AccessibleName="仮眠時間（分）"; inputs.Controls.Add(clock); inputs.Controls.Add(minutes); root.Controls.Add(inputs,0,5);
            volumeLabel.Dock=DockStyle.Fill; root.Controls.Add(volumeLabel,0,6); volume.Minimum=1; volume.Maximum=50; volume.Value=25; volume.TickStyle=TickStyle.None; volume.Dock=DockStyle.Fill; volume.AccessibleName="音量"; volume.ValueChanged+=delegate {volumeLabel.Text="音量  "+volume.Value+"%";}; volumeLabel.Text="音量  25%"; root.Controls.Add(volume,0,7);
            var soundRow=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2}; soundRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); soundRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100)); sounds.DropDownStyle=ComboBoxStyle.DropDownList; sounds.Items.AddRange(new object[]{"パルス","ウェーブ","チャイム"}); sounds.SelectedIndex=0; sounds.Dock=DockStyle.Fill; sounds.AccessibleName="アラーム音"; soundRow.Controls.Add(sounds,0,0); preview.Text="試聴"; preview.Dock=DockStyle.Fill; preview.Click+=PlayPreview; soundRow.Controls.Add(preview,1,0); root.Controls.Add(soundRow,0,8);
            remaining.Text="--:--:--"; remaining.TextAlign=ContentAlignment.MiddleCenter; remaining.Font=new Font("Segoe UI",32,FontStyle.Bold); remaining.Dock=DockStyle.Fill; root.Controls.Add(remaining,0,9);
            var actions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3}; for(int i=0;i<3;i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.33f));
            start.Text="セットする"; start.BackColor=accent; start.ForeColor=Color.White; start.FlatStyle=FlatStyle.Flat; stop.Text="停止"; snooze.Text="3分後に再通知"; start.Dock=stop.Dock=snooze.Dock=DockStyle.Fill;
            start.Click+=delegate {Arm(null);}; stop.Click+=delegate {StopAlarm(false,"");}; snooze.Click+=delegate {if(alarm.State==AlarmState.Ringing) Arm(TimeSpan.FromMinutes(3));}; actions.Controls.Add(start,0,0); actions.Controls.Add(stop,1,0); actions.Controls.Add(snooze,2,0); root.Controls.Add(actions,0,10);
            state.Text="イヤホンを接続してください"; state.TextAlign=ContentAlignment.MiddleCenter; state.Dock=DockStyle.Fill; root.Controls.Add(state,0,11);
            outputs.SelectedIndexChanged+=delegate {if(alarm.State==AlarmState.Armed||alarm.State==AlarmState.Ringing) StopAlarm(true,"出力先が変更されました"); UpdateButtons();};
            player.Failed+=delegate(string message) { try { if(!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate {StopAlarm(true,message);}); } catch(InvalidOperationException) {} };
            sounds.DrawMode=DrawMode.OwnerDrawFixed; sounds.ItemHeight=24;
            sounds.DrawItem+=delegate(object sender,DrawItemEventArgs e) {e.DrawBackground(); if(e.Index>=0) TextRenderer.DrawText(e.Graphics,sounds.Items[e.Index].ToString(),e.Font,e.Bounds,SystemColors.ControlText,TextFormatFlags.VerticalCenter|TextFormatFlags.Left);e.DrawFocusRectangle();};
            FormClosing+=OnClosing; if(!demo) SystemEvents.PowerModeChanged+=PowerChanged;
            tick.Interval=200; tick.Tick+=OnTick; RefreshDevices(); UpdateMode(); UpdateButtons(); if(!demo) tick.Start();
        }
        Headphone Selected {get {return outputs.SelectedItem as Headphone;}}
        void UpdateMode() {clock.Visible=timeMode.Checked; minutes.Visible=!timeMode.Checked;}
        void RefreshDevices() {
            string selected=Selected==null?null:Selected.Id;
            try {
                var devices=demo ? new System.Collections.Generic.List<Headphone>() : AudioDevices.List();
                outputs.BeginUpdate(); outputs.Items.Clear(); foreach(var h in devices) outputs.Items.Add(h); outputs.EndUpdate();
                for(int i=0;i<outputs.Items.Count;i++) if(((Headphone)outputs.Items[i]).Id==selected) outputs.SelectedIndex=i;
                if(outputs.SelectedIndex<0 && outputs.Items.Count>0) outputs.SelectedIndex=0;
                routeStatus.Text=devices.Count==0 ? "対応するイヤホンが未接続です" : "イヤホン専用・スピーカーへの切り替えなし";
            } catch(Exception) {routeStatus.Text="出力先を確認できません";}
            UpdateButtons();
        }
        void Arm(TimeSpan? snoozeTime) {
            var h=Selected; if(h==null || !AudioDevices.IsActive(h.Id)) {StopAlarm(true,"イヤホンを接続してください"); RefreshDevices();return;}
            try {
                player.Stop(); previewUntil=DateTimeOffset.MinValue; var now=DateTimeOffset.Now; bool timer=snoozeTime.HasValue||timerMode.Checked;
                var target=snoozeTime.HasValue ? now+snoozeTime.Value : timer ? now.AddMinutes((double)minutes.Value) : AlarmTime.NextLocal(clock.Value.TimeOfDay,now,TimeZoneInfo.Local);
                // The picker displays minutes, so discard inherited seconds.
                if(!timer) target=AlarmTime.NextLocal(new TimeSpan(clock.Value.Hour,clock.Value.Minute,0),now,TimeZoneInfo.Local);
                alarm.Arm(h.Id,target,timer,now,monotonic.Elapsed); pinnedId=h.Id; SetThreadExecutionState(0x80000001);
                state.Text=target.ToLocalTime().ToString("M/d HH:mm")+" に鳴ります"; UpdateButtons();
            } catch(Exception) {StopAlarm(true,"アラームを設定できませんでした");}
        }
        void PlayPreview(object sender,EventArgs e) {
            var h=Selected; if(h==null)return;
            try {pinnedId=h.Id; player.Play(h.Id,volume.Value/100.0,sounds.SelectedIndex,3);previewUntil=DateTimeOffset.Now.AddSeconds(4);state.Text="試聴中";UpdateButtons();}
            catch(Exception) {StopAlarm(true,"イヤホンへの再生を開始できません");}
        }
        void OnTick(object sender,EventArgs e) {
            bool active=alarm.State==AlarmState.Armed||alarm.State==AlarmState.Ringing;
            if(active) {
                bool allowed=AudioDevices.IsActive(alarm.EndpointId);
                if(alarm.Poll(DateTimeOffset.Now,monotonic.Elapsed,allowed)) {
                    try {player.Play(alarm.EndpointId,volume.Value/100.0,sounds.SelectedIndex,600);state.Text="起きる時間です";}
                    catch(Exception) {StopAlarm(true,"イヤホンへの再生を開始できません");}
                }
                if(alarm.State==AlarmState.StoppedForSafety) {StopAlarm(true,alarm.Reason);return;}
                var left=alarm.Remaining(DateTimeOffset.Now,monotonic.Elapsed); remaining.Text=String.Format("{0:00}:{1:00}:{2:00}",(int)left.TotalHours,left.Minutes,left.Seconds);
                if(alarm.State==AlarmState.Ringing && DateTimeOffset.Now>alarm.Target.AddMinutes(10)) StopAlarm(false,"");
            }
            if(previewUntil!=DateTimeOffset.MinValue) {
                if(!AudioDevices.IsActive(pinnedId)) {StopAlarm(true,"イヤホンが切断されました");return;}
                if(DateTimeOffset.Now>=previewUntil) {player.Stop();previewUntil=DateTimeOffset.MinValue;state.Text="試聴終了";}
            }
            if(!active && previewUntil==DateTimeOffset.MinValue && ++ticks%15==0) RefreshDevices();
            UpdateButtons();
        }
        void StopAlarm(bool safety,string message) {
            player.Stop(); previewUntil=DateTimeOffset.MinValue; SetThreadExecutionState(0x80000000);
            if(safety) alarm.SafetyStop(message); else alarm.Stop();
            state.Text=safety?message:"停止しました"; remaining.Text="--:--:--"; UpdateButtons();
        }
        void UpdateButtons() {
            bool active=alarm.State==AlarmState.Armed||alarm.State==AlarmState.Ringing; bool testing=previewUntil!=DateTimeOffset.MinValue;
            start.Enabled=Selected!=null&&!active&&!testing; preview.Enabled=start.Enabled; stop.Enabled=active||testing; snooze.Enabled=alarm.State==AlarmState.Ringing;
            outputs.Enabled=refresh.Enabled=sounds.Enabled=volume.Enabled=timeMode.Enabled=timerMode.Enabled=clock.Enabled=minutes.Enabled=!active&&!testing;
        }
        void PowerChanged(object sender,PowerModeChangedEventArgs e) {
            if(e.Mode==PowerModes.Suspend && !IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate {StopAlarm(true,"スリープしたため停止しました");});
        }
        void OnClosing(object sender,FormClosingEventArgs e) {
            if(!demo && e.CloseReason==CloseReason.UserClosing && (alarm.State==AlarmState.Armed||alarm.State==AlarmState.Ringing)) {
                if(MessageBox.Show("終了するとアラームも停止します。終了しますか？","HUSH Alarm",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) {e.Cancel=true;return;}
            }
            tick.Stop(); StopAlarm(false,""); player.Dispose(); if(!demo) SystemEvents.PowerModeChanged-=PowerChanged;
        }
    }
}

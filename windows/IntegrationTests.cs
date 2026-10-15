using System;
using System.IO;
using HushAlarm;
class IntegrationTests {
    static int count;
    static void Check(bool value,string name) {if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    [STAThread] static void Main(string[] args) {
        var headphones=AudioDevices.List();
        Check(headphones!=null,"real Windows audio enumeration succeeds");
        foreach(var h in headphones) Check(AlarmTime.IsHeadphoneFormFactor(h.FormFactor)&&AudioDevices.IsActive(h.Id),"listed endpoint is active and classified as headphones");
        Check(!AudioDevices.IsActive("default"),"default endpoint alias denied on actual OS");
        Check(!AudioDevices.IsActive("not-a-real-endpoint"),"missing endpoint denied on actual OS");
        using(var player=new HeadphonePlayer()) {
            bool rejected=false; try {player.Play("default",0.1,0,0.1);}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"playback cannot open default speaker alias");
        }
        LocalAuth.TestStoragePath=Path.GetFullPath(Path.Combine("build","windows","test-auth.dat"));
        LocalAuth.Create("864209"); int wait;
        Check(LocalAuth.Verify("864209",out wait),"PIN verifier round trip through Windows encryption");
        Check(!LocalAuth.Verify("864208",out wait),"incorrect PIN rejected");
        for(int i=0;i<4;i++) LocalAuth.Verify("864208",out wait);
        Check(!LocalAuth.Verify("864209",out wait)&&wait>0,"rate limiting blocks repeated attempts");
        Check(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(LocalAuth.TestStoragePath)).IndexOf("864209",StringComparison.Ordinal)<0,"PIN is not stored in plaintext");
        Console.WriteLine("Passed "+count+" integration tests; no audio was played.");
    }
}

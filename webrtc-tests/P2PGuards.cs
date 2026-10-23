using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Runtime.Loader;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

class P2PGuards
{
    static int Main(string[] args)
    {
        string bin = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            string path = Path.Combine(bin, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "WebRtcVoiceRegionModule.dll"));
        Type moduleType = assembly.GetType("osWebRtcVoice.WebRtcVoiceRegionModule", true);
        object module = Activator.CreateInstance(moduleType);
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        Type callType = moduleType.GetNestedType("P2PVoiceCall", BindingFlags.NonPublic);
        object call = Activator.CreateInstance(callType, true);
        UUID caller = UUID.Random(), callee = UUID.Random(), scene = UUID.Random();
        void Set(string field, object value) => callType.GetField(field).SetValue(call, value);
        Set("Caller", caller); Set("Callee", callee); Set("Scene", scene);
        Set("Channel", "private-test-channel"); Set("Credentials", "private-test-secret");
        Set("Expires", DateTime.UtcNow.AddMinutes(2));
        IDictionary calls = (IDictionary)moduleType.GetField("m_P2PCalls", hidden).GetValue(module);
        UUID session = UUID.Random();
        calls.Add(session, call);
        MethodInfo authorize = moduleType.GetMethod("AuthorizeP2P", hidden);
        OSDMap request = new() { ["channel"] = OSD.FromString("private-test-channel"), ["credentials"] = OSD.FromString("private-test-secret") };
        int count = 0;
        void Check(string label, UUID agent, UUID region, bool expected) {
            bool actual = (bool)authorize.Invoke(module, new object[] {request, agent, region});
            if (actual != expected) throw new Exception(label + " failed");
            Console.WriteLine("PASS " + label); count++;
        }
        Check("caller may provision pending call", caller, scene, true);
        Check("callee cannot join before acceptance", callee, scene, false);
        Check("third avatar denied", UUID.Random(), scene, false);
        Check("other scene denied", caller, UUID.Random(), false);
        request["credentials"] = OSD.FromString("incorrect");
        Check("incorrect credential denied", caller, scene, false);
        request["credentials"] = OSD.FromString("private-test-secret");
        Set("Accepted", true);
        Check("accepted callee may provision", callee, scene, true);
        Set("Expires", DateTime.UtcNow.AddMinutes(-1));
        Check("expired call denied", caller, scene, false);
        Set("Expires", DateTime.UtcNow.AddMinutes(2));
        IDictionary viewers = (IDictionary)moduleType.GetField("m_P2PViewerSessions", hidden).GetValue(module);
        viewers.Add("viewer-test", (session, caller));
        MethodInfo endCall = moduleType.GetMethod("EndP2P", hidden);
        endCall.Invoke(module, new object[] { "viewer-test", UUID.Random() });
        Check("other avatar cannot end call", caller, scene, true);
        endCall.Invoke(module, new object[] { "viewer-test", caller });
        Check("hangup invalidates caller credentials", caller, scene, false);
        Check("hangup invalidates callee credentials", callee, scene, false);
        if (viewers.Count != 0) throw new Exception("viewer state retained after hangup");
        Console.WriteLine("PASS viewer state removed after hangup"); count++;
        Console.WriteLine(count + " guard checks passed");
        return 0;
    }
}

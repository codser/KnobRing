using System;
using System.Threading;

namespace KnobRing
{
    // KnobRing and "KnobRing for Premiere" are built from the same code. build.sh / build.bat compile the
    // Premiere app with -define:PREMIERE. Everything that must differ between the two lives here.
    internal static class Edition
    {
#if PREMIERE
        public static readonly bool IsPremiere = true;
        public const string Name = "KnobRing for Premiere";
        public const string ExeBase = "KnobRingPremiere";              // KnobRingPremiere.exe, .update.exe, .old.exe
        public const string DataFolder = "KnobRing Premiere";          // %APPDATA%\KnobRing Premiere
        public const string MutexName = "KnobRingPremiere_single";
        public const string OpenEventName = "KnobRingPremiere_open_window";
        public const string StartupValue = "KnobRing for Premiere";
        public const string ExtensionFolder = "KnobRingPremiere";      // %APPDATA%\Adobe\CEP\extensions\...
        public const string DefaultsResource = "rings.premiere.json";
#else
        public static readonly bool IsPremiere = false;
        public const string Name = "KnobRing";
        public const string ExeBase = "KnobRing";
        public const string DataFolder = "KnobRing";
        public const string MutexName = "KnobRing_AULA_F75_single";
        public const string OpenEventName = "KnobRing_AULA_F75_open_window";
        public const string StartupValue = "KnobRing";
        public const string ExtensionFolder = "KnobRingBridge";
        public const string DefaultsResource = "rings.default.json";
#endif
        public const string PremiereExe = "adobe premiere pro.exe";

        // Is "KnobRing for Premiere" running? KnobRing then leaves the knob alone while Premiere is in front.
        public static bool PremiereAppRunning()
        {
            Mutex m;
            if (Mutex.TryOpenExisting("KnobRingPremiere_single", out m)) { m.Dispose(); return true; }
            return false;
        }
    }
}

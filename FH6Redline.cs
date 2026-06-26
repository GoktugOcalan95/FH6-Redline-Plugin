using System;
using System.Collections.Generic;
using GameReaderCommon;
using SimHub.Plugins;

namespace FH6Redline
{
    [PluginName("FH6Redline")]
    [PluginDescription("Learns the usable rev-out RPM of Forza Horizon 6 cars and exposes a shift-light proximity value.")]
    [PluginAuthor("FH6Redline")]
    public class FH6Redline : IPlugin, IDataPlugin
    {
        public PluginManager PluginManager { get; set; }

        // ---- tuning ----------------------------------------------------------------------
        private const double MinThrottlePercent = 95.0;   // near-WOT gate
        private const double MinOnsetFraction   = 0.85;   // rev-out onset sits at least this high
        private const double DropFloorRpm       = 40.0;   // reversal needed to call the onset...
        private const double DropFraction       = 0.004;  // ...or this fraction of MaxRpm, whichever is larger
        private const double ReArmFraction      = 0.85;   // rpm below peak*this starts a new climb
        private const int    SampleCount        = 5;      // rev-outs averaged before locking
        private const int    RpmOffset          = 100;     // subtracted from the average to get the shift point
        private const string StoreKey           = "Cars";

        // Per-gear proximity window. Low gears rev out fast, so a wider window keeps the shift
        // LEDs lit for a similar real-time duration across all gears. Index = gear (1..10).
        private static readonly double[] GearWindow =
            { 0.40, 0.40, 0.30, 0.20, 0.12, 0.10, 0.08, 0.08, 0.08, 0.08, 0.08 };

        // ---- state -----------------------------------------------------------------------
        private CarStore _store;
        private string _key = "";
        private bool _done;
        private int _finalRpm;
        private double _sum;
        private int _count;
        private double _peak;
        private bool _committed;

        // ---- outputs ---------------------------------------------------------------------
        private int _outRpm;
        private double _outProx;
        private string _outKey = "";

        public void Init(PluginManager pluginManager)
        {
            _store = this.ReadCommonSettings<CarStore>(StoreKey, () => new CarStore());
            this.AttachDelegate("MaxUsableRpm", () => _outRpm);
            this.AttachDelegate("RevLimiterProximity", () => _outProx);
            this.AttachDelegate("CurrentCarKey", () => _outKey);
        }

        public void End(PluginManager pluginManager)
        {
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (!data.GameRunning || data.NewData == null) return;
            if (data.GameName != "FH6") return;

            StatusDataBase nd = data.NewData;
            double maxRpm = nd.MaxRpm;
            if (maxRpm <= 0) return;

            double rpm = nd.Rpms;
            double throttle = nd.Throttle;
            if (throttle <= 1.0) throttle *= 100.0;

            string key = BuildKey(pluginManager, maxRpm);
            if (key != _key) SwitchCar(key);
            _outKey = key;

            if (!_done)
            {
                if (throttle >= MinThrottlePercent) Detect(rpm, maxRpm);
                else { _peak = 0; _committed = false; }
            }

            int shift = CurrentShift();
            _outRpm = shift;
            _outProx = Proximity(rpm, shift, GearWindow[GearIndex(nd.Gear)]);
        }

        // Capture the climb peak at the first reversal near the top. Gear is not considered.
        private void Detect(double rpm, double maxRpm)
        {
            if (rpm < _peak * ReArmFraction) { _peak = rpm; _committed = false; }
            if (rpm > _peak) _peak = rpm;
            if (_committed) return;

            double drop = Math.Max(DropFloorRpm, maxRpm * DropFraction);
            if (_peak >= maxRpm * MinOnsetFraction && (_peak - rpm) >= drop)
            {
                _sum += _peak;
                _count++;
                _committed = true;
                if (_count >= SampleCount) Lock();
            }
        }

        private void Lock()
        {
            _finalRpm = (int)Math.Round(_sum / _count) - RpmOffset;
            _done = true;
            // Re-read first so a hand-edit to another car is merged, not overwritten.
            _store = this.ReadCommonSettings<CarStore>(StoreKey, () => new CarStore());
            _store.Cars[_key] = _finalRpm;
            this.SaveCommonSettings(StoreKey, _store);
        }

        private int CurrentShift()
        {
            if (_done) return _finalRpm;
            if (_count == 0) return 0;
            return (int)Math.Round(_sum / _count) - RpmOffset;
        }

        private void SwitchCar(string key)
        {
            _key = key;
            _peak = 0; _committed = false; _sum = 0; _count = 0;
            int v;
            if (_store.Cars.TryGetValue(key, out v)) { _finalRpm = v; _done = true; }
            else { _finalRpm = 0; _done = false; }
        }

        private static double Proximity(double rpm, int shift, double window)
        {
            if (shift <= 0) return 0.0;
            double start = shift * (1.0 - window);
            if (rpm <= start) return 0.0;
            if (rpm >= shift) return 1.0;
            return (rpm - start) / (shift - start);
        }

        private static int GearIndex(string gear)
        {
            int g;
            if (!int.TryParse(gear, out g)) g = 1; // R / N / D fall back to the gear-1 window
            if (g < 1) g = 1;
            if (g > 10) g = 10;
            return g;
        }

        // Car key = CarOrdinal_CarPerformanceIndex_MaxRpm. The two raw values come from SimHub's
        // Forza property tree.
        private static string BuildKey(PluginManager pm, double maxRpm)
        {
            int ordinal = ReadInt(pm, "DataCorePlugin.GameRawData.CarOrdinal");
            int pi = ReadInt(pm, "DataCorePlugin.GameRawData.CarPerformanceIndex");
            return ordinal + "_" + pi + "_" + (int)Math.Round(maxRpm);
        }

        private static int ReadInt(PluginManager pm, string prop)
        {
            try
            {
                object v = pm.GetPropertyValue(prop);
                return v == null ? 0 : Convert.ToInt32(v);
            }
            catch { return 0; }
        }
    }

    // Persisted map of car key -> locked shift RPM, stored under PluginsData\Common.
    public class CarStore
    {
        public Dictionary<string, int> Cars { get; set; } = new Dictionary<string, int>();
    }
}

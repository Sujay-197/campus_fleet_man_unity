using UnityEngine;

namespace BusSystem
{
    /// <summary>Demand-rate multiplier over a 24h day: a base level plus two rush-hour bumps.</summary>
    public static class PeakProfile
    {
        public static float Multiplier(float timeOfDayHours)
        {
            float morning = Gaussian(timeOfDayHours, 8f, 1.2f);
            float evening = Gaussian(timeOfDayHours, 17f, 1.2f);
            return 0.3f + 2.5f * (morning + evening);
        }

        static float Gaussian(float x, float mean, float sigma)
        {
            float d = (x - mean) / sigma;
            return Mathf.Exp(-0.5f * d * d);
        }
    }
}

using System;
using System.Collections.Generic;

namespace TizenAmbilight
{
    /// <summary>Discover complete Samsung RGB-measure families without mixing firmware ABIs.</summary>
    public static class RgbMeasureApi
    {
        public sealed class Family
        {
            public string Name;
            public IntPtr Condition, Position, Pixel;
        }

        public static List<Family> Discover(Func<string, IntPtr> resolve, string platformVersion,
                                            out string report)
        {
            int major;
            bool older = int.TryParse((platformVersion ?? "").Split('.')[0], out major) && major < 7;
            string[] names = older ? new[] { "cs_ve", "ve", "ppi_ve" }
                                   : new[] { "ppi_ve", "ve", "cs_ve" };
            var found = new List<Family>();
            var statuses = new List<string>();
            foreach (string name in names)
            {
                var f = new Family
                {
                    Name = name,
                    Condition = resolve(name + "_get_rgb_measure_condition"),
                    Position = resolve(name + "_set_rgb_measure_position"),
                    Pixel = resolve(name + "_get_rgb_measure_pixel")
                };
                bool complete = f.Condition != IntPtr.Zero && f.Position != IntPtr.Zero && f.Pixel != IntPtr.Zero;
                statuses.Add(name + ":condition=" + (f.Condition != IntPtr.Zero) +
                             ",position=" + (f.Position != IntPtr.Zero) + ",pixel=" + (f.Pixel != IntPtr.Zero));
                if (complete) found.Add(f);
            }
            report = string.Join("; ", statuses.ToArray());
            return found;
        }

        public static bool ValidCondition(int slots, int blockW, int blockH, int width, int height)
        {
            return slots > 0 && slots <= 16 && width > 0 && width <= 16384 &&
                   height > 0 && height <= 16384 && blockW > 0 && blockW < width &&
                   blockH > 0 && blockH < height;
        }
    }
}

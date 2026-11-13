// L0303 M-5: the contrast probe of one caption box in a screen capture. Shared by PanelColourScenes.ps1
// (its -SelfTest compiles this file with Add-Type) and the scene harness the same script builds.
using System;
using System.Collections.Generic;
using System.Drawing;

public static class PanelColourProbe {
    // WCAG 2 relative luminance of one sRGB colour.
    public static double Luminance(Color c) {
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    static double Linear(int channel) {
        double v = channel / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    public static double Contrast(double a, double b) {
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    // The box is split into two luminance clusters at the Otsu threshold; the smaller cluster is the ink (strokes
    // cover less of a caption box than its backdrop), the larger the backdrop around it. The contrast of the two
    // cluster medians is returned, or NaN when the box holds a single cluster (nothing drawn) or is empty.
    public static double MeasureBox(Bitmap capture, Rectangle box) {
        box.Intersect(new Rectangle(Point.Empty, capture.Size));
        if (box.Width <= 0 || box.Height <= 0) return double.NaN;
        var values = new List<double>(box.Width * box.Height);
        for (int y = box.Top; y < box.Bottom; y++)
            for (int x = box.Left; x < box.Right; x++)
                values.Add(Luminance(capture.GetPixel(x, y)));
        double threshold;
        if (!OtsuThreshold(values, out threshold)) return double.NaN;
        var low = new List<double>();
        var high = new List<double>();
        foreach (double v in values) (v <= threshold ? low : high).Add(v);
        if (low.Count == 0 || high.Count == 0) return double.NaN;
        List<double> ink = low.Count < high.Count ? low : high;
        List<double> backdrop = ReferenceEquals(ink, low) ? high : low;
        return Contrast(Median(ink), Median(backdrop));
    }

    static bool OtsuThreshold(List<double> values, out double threshold) {
        const int bins = 256;
        var histogram = new int[bins];
        foreach (double v in values) histogram[Math.Min(bins - 1, (int)(v * bins))]++;
        int total = values.Count;
        double sumAll = 0;
        for (int i = 0; i < bins; i++) sumAll += i * (double)histogram[i];
        double sumLow = 0, best = -1;
        int weightLow = 0, bestBin = -1;
        for (int i = 0; i < bins; i++) {
            weightLow += histogram[i];
            if (weightLow == 0) continue;
            int weightHigh = total - weightLow;
            if (weightHigh == 0) break;
            sumLow += i * (double)histogram[i];
            double meanLow = sumLow / weightLow;
            double meanHigh = (sumAll - sumLow) / weightHigh;
            double between = (double)weightLow * weightHigh * (meanLow - meanHigh) * (meanLow - meanHigh);
            if (between > best) { best = between; bestBin = i; }
        }
        threshold = (bestBin + 1) / (double)bins;
        return bestBin >= 0 && best > 0;
    }

    static double Median(List<double> values) {
        values.Sort();
        return values[values.Count / 2];
    }

    // Nearest-rank percentile (the rank the timing reader uses); NaN entries are skipped.
    public static double Percentile(IEnumerable<double> values, double fraction) {
        var list = new List<double>();
        foreach (double v in values) if (!double.IsNaN(v)) list.Add(v);
        if (list.Count == 0) return double.NaN;
        list.Sort();
        int rank = Math.Max(1, (int)Math.Ceiling(list.Count * fraction));
        return list[rank - 1];
    }
}

using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar3D.Classes.Toolbox
{
    /// <summary>
    /// Square Hollow Section (SHS) catalog.
    /// Each entry stores the outer dimension and the valid thickness range
    /// (filtered to t >= 4 mm, 1 mm step).
    /// </summary>
    public static class SHS_Catalog
    {
        public struct SizeEntry
        {
            public int Size;
            public int Tmin;
            public int Tmax;

            public SizeEntry(int size, int tmin, int tmax)
            {
                Size = size;
                Tmin = tmin;
                Tmax = tmax;
            }

            public int[] ValidThicknesses()
            {
                var list = new List<int>();
                for (int t = Tmin; t <= Tmax; t++)
                    list.Add(t);
                return list.ToArray();
            }

            public int ThicknessCount => Tmax - Tmin + 1;
        }

        public static readonly SizeEntry[] Entries = new SizeEntry[]
        {
            new SizeEntry(30, 4, 4),
            new SizeEntry(32, 4, 4),
            new SizeEntry(35, 4, 4),
            new SizeEntry(38, 4, 4),
            new SizeEntry(40, 4, 4),
            new SizeEntry(44, 4, 4),
            new SizeEntry(45, 4, 5),
            new SizeEntry(50, 4, 5),
            new SizeEntry(52, 4, 5),
            new SizeEntry(60, 4, 5),
            new SizeEntry(70, 4, 6),
            new SizeEntry(75, 4, 6),
            new SizeEntry(76, 4, 6),
            new SizeEntry(80, 4, 8),
            new SizeEntry(85, 4, 8),
            new SizeEntry(90, 4, 8),
            new SizeEntry(95, 4, 8),
            new SizeEntry(100, 4, 8),
            new SizeEntry(120, 4, 8),
            new SizeEntry(125, 4, 8),
            new SizeEntry(130, 4, 8),
            new SizeEntry(140, 6, 10),
            new SizeEntry(150, 6, 10),
            new SizeEntry(160, 6, 10),
            new SizeEntry(180, 6, 12),
            new SizeEntry(200, 6, 30),
            new SizeEntry(220, 6, 30),
            new SizeEntry(250, 6, 30),
            new SizeEntry(270, 6, 30),
            new SizeEntry(280, 6, 30),
            new SizeEntry(300, 8, 30),
            new SizeEntry(320, 8, 30),
            new SizeEntry(350, 8, 30),
            new SizeEntry(380, 8, 30),
            new SizeEntry(400, 8, 30),
            new SizeEntry(420, 10, 30),
            new SizeEntry(450, 10, 30),
            new SizeEntry(480, 10, 30),
            new SizeEntry(500, 10, 30),
            new SizeEntry(550, 10, 40),
            new SizeEntry(600, 10, 40),
            new SizeEntry(700, 10, 40),
            new SizeEntry(800, 10, 50),
            new SizeEntry(900, 10, 50),
            new SizeEntry(1000, 10, 50),
        };

        public static int[] AllSizes => Entries.Select(e => e.Size).ToArray();

        public static SizeEntry? FindBySize(int size)
        {
            for (int i = 0; i < Entries.Length; i++)
                if (Entries[i].Size == size) return Entries[i];
            return null;
        }

        /// <summary>
        /// Returns a flat list of all valid (Size, Thickness) pairs,
        /// ordered by ascending size then ascending thickness.
        /// Useful for cross-section optimization indexing.
        /// </summary>
        public static List<(int Size, int T)> AllCombinations()
        {
            var list = new List<(int, int)>();
            foreach (var e in Entries)
                for (int t = e.Tmin; t <= e.Tmax; t++)
                    list.Add((e.Size, t));
            return list;
        }
    }
}

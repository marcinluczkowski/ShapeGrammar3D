using System.Collections.Generic;
using System.Linq;

namespace ShapeGrammar3D.Classes.Toolbox
{
    /// <summary>
    /// Standard European HEA and HEB I-section catalog (EN 10365).
    /// Dimensions in mm.
    /// </summary>
    public static class HEA_Catalog
    {
        public struct ProfileEntry
        {
            public string Name;
            public double H;   // total height
            public double W;   // flange width
            public double Tw;  // web thickness
            public double Tf;  // flange thickness

            public ProfileEntry(string name, double h, double w, double tw, double tf)
            { Name = name; H = h; W = w; Tw = tw; Tf = tf; }
        }

        public static readonly ProfileEntry[] HEA = new ProfileEntry[]
        {
            new ProfileEntry("HEA100",  96, 100, 5.0,  8.0),
            new ProfileEntry("HEA120", 114, 120, 5.0,  8.0),
            new ProfileEntry("HEA140", 133, 140, 5.5,  8.5),
            new ProfileEntry("HEA160", 152, 160, 6.0,  9.0),
            new ProfileEntry("HEA180", 171, 180, 6.0,  9.5),
            new ProfileEntry("HEA200", 190, 200, 6.5, 10.0),
            new ProfileEntry("HEA220", 210, 220, 7.0, 11.0),
            new ProfileEntry("HEA240", 230, 240, 7.5, 12.0),
            new ProfileEntry("HEA260", 250, 260, 7.5, 12.5),
            new ProfileEntry("HEA280", 270, 280, 8.0, 13.0),
            new ProfileEntry("HEA300", 290, 300, 8.5, 14.0),
            new ProfileEntry("HEA320", 310, 300, 9.0, 15.5),
            new ProfileEntry("HEA340", 330, 300, 9.5, 16.5),
            new ProfileEntry("HEA360", 350, 300,10.0, 17.5),
            new ProfileEntry("HEA400", 390, 300,11.0, 19.0),
            new ProfileEntry("HEA450", 440, 300,11.5, 21.0),
            new ProfileEntry("HEA500", 490, 300,12.0, 23.0),
            new ProfileEntry("HEA550", 540, 300,12.5, 24.0),
            new ProfileEntry("HEA600", 590, 300,13.0, 25.0),
            new ProfileEntry("HEA650", 640, 300,13.5, 26.0),
            new ProfileEntry("HEA700", 690, 300,14.5, 27.0),
            new ProfileEntry("HEA800", 790, 300,15.0, 28.0),
            new ProfileEntry("HEA900", 890, 300,16.0, 30.0),
            new ProfileEntry("HEA1000",990, 300,16.5, 31.0),
        };

        public static readonly ProfileEntry[] HEB = new ProfileEntry[]
        {
            new ProfileEntry("HEB100", 100, 100, 6.0, 10.0),
            new ProfileEntry("HEB120", 120, 120, 6.5, 11.0),
            new ProfileEntry("HEB140", 140, 140, 7.0, 12.0),
            new ProfileEntry("HEB160", 160, 160, 8.0, 13.0),
            new ProfileEntry("HEB180", 180, 180, 8.5, 14.0),
            new ProfileEntry("HEB200", 200, 200, 9.0, 15.0),
            new ProfileEntry("HEB220", 220, 220, 9.5, 16.0),
            new ProfileEntry("HEB240", 240, 240,10.0, 17.0),
            new ProfileEntry("HEB260", 260, 260,10.0, 17.5),
            new ProfileEntry("HEB280", 280, 280,10.5, 18.0),
            new ProfileEntry("HEB300", 300, 300,11.0, 19.0),
            new ProfileEntry("HEB320", 320, 300,11.5, 20.5),
            new ProfileEntry("HEB340", 340, 300,12.0, 21.5),
            new ProfileEntry("HEB360", 360, 300,12.5, 22.5),
            new ProfileEntry("HEB400", 400, 300,13.5, 24.0),
            new ProfileEntry("HEB450", 450, 300,14.0, 26.0),
            new ProfileEntry("HEB500", 500, 300,14.5, 28.0),
            new ProfileEntry("HEB550", 550, 300,15.0, 29.0),
            new ProfileEntry("HEB600", 600, 300,15.5, 30.0),
            new ProfileEntry("HEB650", 650, 300,16.0, 31.0),
            new ProfileEntry("HEB700", 700, 300,17.0, 32.0),
            new ProfileEntry("HEB800", 800, 300,17.5, 33.0),
            new ProfileEntry("HEB900", 900, 300,18.5, 35.0),
            new ProfileEntry("HEB1000",1000,300,19.0, 36.0),
        };

        /// <summary>
        /// Returns all HEA + HEB profiles sorted by cross-section area (ascending).
        /// </summary>
        public static List<ProfileEntry> AllProfiles()
        {
            var all = new List<ProfileEntry>();
            all.AddRange(HEA);
            all.AddRange(HEB);
            return all.OrderBy(p => ComputeArea(p)).ToList();
        }

        public static double ComputeArea(ProfileEntry p)
        {
            return p.W * p.H - (p.W - p.Tw) * (p.H - 2.0 * p.Tf);
        }
    }
}

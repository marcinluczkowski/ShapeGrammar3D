using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShapeGrammar3D.Classes
{
    [Serializable]
    public class SG_Genotype
    {
        // --- properties ---
        public List<int> IntGenes { get; set; } = new List<int>();
        public List<double> DGenes { get; set; } = new List<double>();

        // --- constructors ---

        public SG_Genotype()
        { 
        }
        public SG_Genotype(List<int> _ints, List<double> _ds)
        {
            IntGenes = _ints;
            DGenes = _ds;
        }

        // --- methods ---

        public List<double> Export()
        {
            List<double> exportLst = new List<double>() { IntGenes.Count, DGenes.Count };
            exportLst.AddRange(IntGenes.Select(n => (double) n).ToList());
            exportLst.AddRange(DGenes);

            return exportLst;
        }

        public void FindRange(ref int _sid, ref int _eid, int _ruleMarker)
        {
            bool fl = false;

            for (int i = 0; i < IntGenes.Count; i++)
            {
                if (IntGenes[i] == 0 || IntGenes[i] == 1) continue;
                if (IntGenes[i] == _ruleMarker)
                {
                    _sid = i + 1;
                    fl = true;
                }
                if (fl == true && IntGenes[i] == UT.RULE_END_MARKER)
                {
                    _eid = i;
                    break;
                }
            }
        }

        internal void FindRange(ref int sid, ref object _, int rULE04_MARKER)
        {
            throw new NotImplementedException();
        }

        public bool TryGetGeneSegment(int ruleMarker, out List<int> intSegment, out List<double> doubleSegment)
        {
            intSegment = new List<int>();
            doubleSegment = new List<double>();

            int start = -1;
            int end = -1;
            FindRange(ref start, ref end, ruleMarker);

            if (start < 0 || end < 0 || start >= end)
            {
                return false;
            }

            var length = end - start;
            intSegment = IntGenes.GetRange(start, Math.Min(length, IntGenes.Count - start));

            if (DGenes.Count > start)
            {
                doubleSegment = DGenes.GetRange(start, Math.Min(length, DGenes.Count - start));
            }

            return true;
        }
    }
}

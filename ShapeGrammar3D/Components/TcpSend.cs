using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using System.Net;
using System.Net.Sockets;
using System.Text;

using System.Threading.Tasks;

using Rhino;
using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Components
{
[System.Obsolete("Archived component: not used by the referenced Grasshopper definitions. Hidden from the toolbar.", false)]
        public class TcpSend : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the tcp_ip_send class.
        /// </summary>
        public TcpSend()
          : base("Tcp IP Send", "TcpipSend",
              "Description",
              UT.CAT, UT.GR_UTIL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("IP Address", "ip address", "", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Port", "port", "", GH_ParamAccess.item);
            pManager.AddTextParameter("Data", "data", "", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- variables ---
            string ip = "";
            int port = new int();
            string txtdata = "";

            // --- input ---
            DA.GetData(0, ref ip);
            DA.GetData(1, ref port);
            DA.GetData(2, ref txtdata);

            // --- solve ---

            try
            {
                TcpClient client = new TcpClient(ip, port);
                NetworkStream stream = client.GetStream();

                // send message
                byte[] initialData = Encoding.ASCII.GetBytes(txtdata);
                stream.Write(initialData, 0, initialData.Length);

                stream.Close();
                client.Close();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Error: " + ex.Message);
            }

            // --- output ---

        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Properties.Resources.icons_CAT_Utilities;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.hidden;

        public override Guid ComponentGuid
        {
            get { return new Guid("48b2d6f6-0d03-492a-b0a4-8493821daedb"); }
        }
    }
}
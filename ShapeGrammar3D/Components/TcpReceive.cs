using System;
using System.Collections.Generic;
using System.Linq;

using System.Net;
using System.Net.Sockets;
using System.Text;

using System.Threading;
using System.Threading.Tasks;

using Rhino;
using Rhino.Geometry;
using Grasshopper.Kernel;

using ShapeGrammar3D.Classes;

namespace ShapeGrammar3D.Components
{
    public class TcpReceive : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the TcpIpReceive class.
        /// </summary>
        public TcpReceive()
          : base("Tcp IP Receive", "TcpipReceive",
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
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "status", "", GH_ParamAccess.item);
            pManager.AddTextParameter("Message", "message", "", GH_ParamAccess.item);
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

            // --- input ---
            DA.GetData(0, ref ip);
            DA.GetData(1, ref port);

            // --- solve ---
            if (_shouldExpire)
            {
                // --- output ---
                DA.SetData(0, _message);
                DA.SetData(1, _data);

                _shouldExpire = false;
                return;
            }

            Listen(ip, port);

            // --- output ---
            DA.SetData(0, _message);
            DA.SetData(1, _data);

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
                return null; //return Properties.Resources.icons_Generic;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("e9d7bc0a-8cca-4039-b9ca-0a4952b81512"); }
        }

        private string _message = "";
        private string _data = "";
        private bool _shouldExpire = false;

        private void Listen(string ip, int port)
        {

            Task.Run(() =>
            {
                int bytesRead;
                byte[] data = new byte[1024];
                TcpListener listener = new TcpListener(IPAddress.Parse(ip), port);
                listener.Start();
                try
                {
                    while (true)
                    {
                        Array.Clear(data, 0, data.Length);

                        var cli = listener.AcceptTcpClient();
                        var stream = cli.GetStream();

                        try
                        {
                            bytesRead = stream.Read(data, 0, data.Length);

                            if (bytesRead > 0)
                            {
                                _message = ((IPEndPoint)cli.Client.RemoteEndPoint).Port.ToString();
                                _data = Encoding.ASCII.GetString(data, 0, bytesRead);

                                _shouldExpire = true;
                                RhinoApp.InvokeOnUiThread((Action)delegate { ExpireSolution(true); });

                                cli.Client.Shutdown(SocketShutdown.Send);
                            }

                        }
                        catch (Exception ex)
                        {
                            RhinoApp.WriteLine("Error TcpIp Connection: {0}", ex.Message);
                        }
                        finally
                        {
                            Thread.Sleep(100);

                            cli.Client.Shutdown(SocketShutdown.Receive);
                            stream.Close();
                            cli.Close();
                        }

                    }

                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine("Error TcpIp outer connection: {0}", ex.Message);
                }
                finally
                {
                    listener.Stop();
                }
            }
      );

        }
    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using NAudio.Wave; // installed with nuget
using NAudio.CoreAudioApi;
using System.Numerics;
using NAudio.Lame;

using System.Net.Sockets;
using System.Net;

using Newtonsoft.Json;
using System.IO;

namespace microphone
{
    public partial class Form1 : Form
    {
        private LameMP3FileWriter wri;

        public WaveIn wi;
        public BufferedWaveProvider bwp;

        private readonly int RATE = 11025; // sample rate of the sound card
        private readonly int BUFFERSIZE = (int) Math.Pow(2,10); // must be a multiple of 2

//        private double START_FREQ = 40.95;
//        private double FREQ_SCALE = 1.221;
        private double START_FREQ = 50;
        private double FREQ_STEP = 170;
        private const int NUM_POI_FREQS = 24;
        private double[] POI_FREQS = new double[NUM_POI_FREQS];
        private int[] POI_FREQS_INDEXIES = new int[NUM_POI_FREQS];
        private double[] accum = new double[NUM_POI_FREQS];
        private int ticks = 0;

        private string LocalIP;
        private UdpClient udp = new UdpClient();

        private string FileDir;
        private string ServerShare;
        private string ServerDir;
        private string TSDB;

        private string Filename;
        private string FullFileServerPath;
        private string FullLocalFilename;
        private string FullServerFilename;

        private readonly string username = "admin";
        private readonly string password = "admin@123456";

        private bool IsRecording = false;

        public Form1()
        {
            InitializeComponent();

            FileDir = System.Configuration.ConfigurationManager.AppSettings["LocalDir"].ToString();
            ServerShare = System.Configuration.ConfigurationManager.AppSettings["FileServerShare"].ToString();
            ServerDir = System.Configuration.ConfigurationManager.AppSettings["FileServerDir"].ToString();
            TSDB = System.Configuration.ConfigurationManager.AppSettings["TSDB"].ToString();

            string hostName = Dns.GetHostName();
            IPAddress[] ipadrlist = Dns.GetHostAddresses(hostName);
            LocalIP = ipadrlist[0].ToString();
            foreach (IPAddress ipa in ipadrlist)
            {
                if (ipa.AddressFamily == AddressFamily.InterNetwork)
                {
                    LocalIP = ipa.ToString();
                    break;
                }
            }

            double k = (double)RATE / (BUFFERSIZE / 2);
            for (int i = 0; i < NUM_POI_FREQS; ++i)
            {
                // POI_FREQS[i] = START_FREQ * Math.Pow(FREQ_SCALE, (double)i);
                POI_FREQS[i] = START_FREQ + i * FREQ_STEP;
                POI_FREQS_INDEXIES[i] = (int)(POI_FREQS[i] / k);
            }
        }

        private string PrepareFileServerDir()
        {
            try
            {
                using (new NetworkConnection(ServerShare, username, password))
                {
                    string p = Path.Combine(ServerShare, ServerDir);
                    if (!Directory.Exists(p))
                    {
                        MessageBox.Show("文件服务器路径无法访问", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return "";
                    }
                    p = Path.Combine(p, DateTime.Now.Year.ToString());
                    p = Path.Combine(p, DateTime.Now.Month.ToString() + "月");
                    if (!Directory.Exists(p))
                    {
                        Directory.CreateDirectory(p);
                    }
                    return p;
                }
            }
            catch (Exception)
            {
                MessageBox.Show("文件服务器连接失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return "";
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            if (!Directory.Exists(FileDir))
            {
                MessageBox.Show("本地文件路径不存在，程序将关闭", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }
            Filename = DateTime.Now.ToString("yyyyMMddHHmm") + ".mp3";
            FullLocalFilename = Path.Combine(FileDir, Filename);
            FullFileServerPath = PrepareFileServerDir();
            if (FullFileServerPath.Length == 0)
            {
                Application.Exit();
                return;
            }
            FullServerFilename = Path.Combine(FullFileServerPath, Filename);

            // see what audio devices are available
            int devcount = WaveIn.DeviceCount;
            Console.Out.WriteLine("Device Count: {0}.", devcount);
            if (devcount == 0)
            {
                MessageBox.Show("无法连接麦克风，程序将关闭", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
//                Application.Exit();
                return;
            }

            // get the WaveIn class started
            wi = new WaveIn
            {
                DeviceNumber = 0,
                WaveFormat = new NAudio.Wave.WaveFormat(RATE, 1)
            };
            // wi.BufferMilliseconds = (int)((double)BUFFERSIZE / (double)RATE * 1000.0);
            Console.WriteLine(wi.BufferMilliseconds);

            // create a wave buffer and start the recording
            wi.DataAvailable += new EventHandler<WaveInEventArgs>(wi_DataAvailable);
            wi.RecordingStopped += waveIn_RecordingStopped;
            bwp = new BufferedWaveProvider(wi.WaveFormat)
            {
                BufferLength = BUFFERSIZE * 2,
                DiscardOnBufferOverflow = true
            };
            //            wi.StartRecording();
            //            StartRecord();
        }

        /*
                private void GetFromServer()
                {
                    try
                    {
                        var jsonText = new WebClient().DownloadString(ServerUrl + "/tsdb");
                        dynamic jo = JsonConvert.DeserializeObject(jsonText);
                        TSDB = jo["tsdb"]["host"];
                        Console.WriteLine(TSDB);
                    }
                    catch (Exception)
                    {
                        MessageBox.Show("无法连接到服务器 " + ServerUrl, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
        */

        private UploadingForm uf;

        private void button3_Click(object sender, EventArgs e)
        {
            uf = new UploadingForm();
            uf.Show();
            CopyFileE(FullLocalFilename, FullServerFilename);
            uf.Close();
        }

        private void CopyFileE(string src, string dst)
        {
            if (!File.Exists(src)) return;
            try
            {
                using (new NetworkConnection(ServerShare, username, password))
                {
                    Action<FileCopyLib.FileProgress> fp = delegate (FileCopyLib.FileProgress s) { OnFileProgress(s); };
                    FileCopyLib.FileCopier.CopyWithProgress(src, dst, fp);
                }
            }
            catch (Exception)
            {
                MessageBox.Show("上传失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnFileProgress(FileCopyLib.FileProgress s)
        {
            uf.SetValue(s.Percentage);
        }

        // adds data to the audio recording buffer
        void wi_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (wri != null)
                wri.Write(e.Buffer, 0, e.BytesRecorded);
            bwp.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        void waveIn_RecordingStopped(object sender, StoppedEventArgs e)
        {
            Console.WriteLine("stopped");
            // flush output to finish MP3 file correctly
            wri.Flush();
            wri.Dispose();
            IsRecording = false;
        }

        public void UpdateAudioGraph()
        {
            // read the bytes from the stream
            int frameSize = BUFFERSIZE;
            var frames = new byte[frameSize];
            bwp.Read(frames, 0, frameSize);
            if (frames.Length == 0) return;
            if (frames[frameSize-2] == 0) return;
            
            timer1.Enabled = false;

            // convert it to int32 manually (and a double for scottplot)
            int SAMPLE_RESOLUTION = 16;
            int BYTES_PER_POINT = SAMPLE_RESOLUTION / 8;
            Int32[] vals = new Int32[frames.Length/BYTES_PER_POINT];
            double[] Ys = new double[frames.Length / BYTES_PER_POINT];
            double[] Xs = new double[frames.Length / BYTES_PER_POINT];
            double[] Ys2 = new double[frames.Length / BYTES_PER_POINT];
            double[] Xs2 = new double[frames.Length / BYTES_PER_POINT];
            for (int i=0; i<vals.Length; i++)
            {
                // bit shift the byte buffer into the right variable format
                byte hByte = frames[i * 2 + 1];
                byte lByte = frames[i * 2 + 0];
                vals[i] = (int)(short)((hByte << 8) | lByte);
                Xs[i] = i;
                Ys[i] = vals[i];
                Xs2[i] = (double)i/Ys.Length*RATE/1000.0; // units are in kHz
            }

            // update scottplot (PCM, time domain)
            scottPlotUC1.Xs = Xs;
            scottPlotUC1.Ys = Ys;

            //update scottplot (FFT, frequency domain)
            const int kc = 5;
            Ys2 = FFT(Ys);
            scottPlotUC2.Xs = Xs2.Take(Xs2.Length / kc).ToArray();
            scottPlotUC2.Ys = Ys2.Take(Ys2.Length / kc).ToArray();

            double[] Xs3 = new double[POI_FREQS_INDEXIES.Length];
            double[] Ys3 = new double[POI_FREQS_INDEXIES.Length];
            for (int i = 0; i < POI_FREQS_INDEXIES.Length; ++i)
            {
                Xs3[i] = (double)i;
                int j = POI_FREQS_INDEXIES[i];
                Ys3[i] = Ys2[j - 2];
                for (int k = -1; k < 3; ++k)
                {
                    double v = Ys2[j + k];
                    if (Ys3[i] < v) Ys3[i] = v;
                }
                if (accum[i] < Ys3[i]) accum[i] = Ys3[i];
            }

            if (Environment.TickCount - ticks > 1000)
            {
//                scottPlotUC3.Xs = Xs3;
//                scottPlotUC3.Ys = accum;
//                scottPlotUC3.SP.AxisSet(0, POI_FREQS_INDEXIES.Length, 0, 1000);
//                scottPlotUC3.UpdateGraph();
                SendToInflux();
                ClearAccum();
            }

            scottPlotUC1.SP.AxisSet(0, BUFFERSIZE / 2, -8000, 8000);
            scottPlotUC2.SP.AxisSet(0, (double)RATE / 1000 / kc, 0, 800);

            // update the displays
            scottPlotUC1.UpdateGraph();
            scottPlotUC2.UpdateGraph();

            Application.DoEvents();
            scottPlotUC1.Update();
            scottPlotUC2.Update();
//            scottPlotUC3.Update();

            timer1.Enabled = true;
        }

        public double[] FFT(double[] data)
        {
            double[] fft = new double[data.Length]; // this is where we will store the output (fft)
            Complex[] fftComplex = new Complex[data.Length]; // the FFT function requires complex format
            for (int i = 0; i < data.Length; i++)
            {
                fftComplex[i] = new Complex(data[i], 0.0); // make it complex format (imaginary = 0)
            }
            Accord.Math.FourierTransform.FFT(fftComplex, Accord.Math.FourierTransform.Direction.Forward);
            for (int i = 0; i < data.Length; i++)
            {
                fft[i] = fftComplex[i].Magnitude; // back to double
                //fft[i] = Math.Log10(fft[i]); // convert to dB
            }
            return fft;
            //todo: this could be much faster by reusing variables
        }

        private void button2_Click(object sender, EventArgs e)
        {
            wi.StopRecording();
        }

        private void StartRecord()
        {
            IsRecording = true;
            wri = new LameMP3FileWriter(FullLocalFilename, wi.WaveFormat, 64);
            ClearAccum();
            UpdateAudioGraph();
            timer1.Enabled = true;
        }

        private void ClearAccum()
        {
            ticks = Environment.TickCount;
            for (int i = 0; i < accum.Length; ++i)
            {
                accum[i] = 0;
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            UpdateAudioGraph();
        }

        private void SendToInflux()
        {
            string s = "surv,ip=" + LocalIP  + " ";
            for (int i = 0; i < accum.Length; ++i)
            {
                s += "c" + String.Format("{0:00}", i) + "=" + String.Format("{0:0.0}", accum[i]) ;
                if (i < accum.Length - 1) s += ",";
            }
            textBox1.Text = DateTime.Now.ToString() + " - " + s;
            Byte[] sendBytes = Encoding.ASCII.GetBytes(s);
            try
            {
                udp.Send(sendBytes, sendBytes.Length, TSDB, 8089);
            }
            catch(Exception e)
            {
                Console.Error.WriteLine(e.ToString());
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            switch (e.CloseReason)
            {
                //自身窗口上的关闭按钮
                case CloseReason.FormOwnerClosing:
                //用户通过UI关闭窗口或者通过Alt+F4关闭窗口
                case CloseReason.UserClosing:
                    e.Cancel = true;//拦截，不响应操作
                    break;
                //应用程序要求关闭窗口
                case CloseReason.ApplicationExitCall:
                    e.Cancel = false; //不拦截，响应操作
                    break;
                //任务管理器关闭进程
                case CloseReason.TaskManagerClosing:
                    e.Cancel = false;//不拦截，响应操作
                    break;
                //操作系统准备关机
                case CloseReason.WindowsShutDown:
                    e.Cancel = false;//不拦截，响应操作
                    break;
                default:
                    break;
            }
        }
    }
}

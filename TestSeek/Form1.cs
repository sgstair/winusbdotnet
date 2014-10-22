/*
Copyright (c) 2014 Stephen Stair (sgstair@akkit.org)

Permission is hereby granted, free of charge, to any person obtaining a copy
 of this software and associated documentation files (the "Software"), to deal
 in the Software without restriction, including without limitation the rights
 to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
 all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 THE SOFTWARE.
*/

// 
// the winusbdotnet repo isn't the correct place for this code long term
// Code is here for now for convenience in testing and iteration while being developed.
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;

using winusbdotnet.UsbDevices;

namespace TestSeek
{
    public partial class Form1 : Form
    {

        SeekThermal thermal;
        Thread thermalThread;
        int frameCount;
        bool stopThread;

        ThermalFrame lastFrame, lastCalibrationFrame;
        CalibratedThermalFrame lastUsableFrame;
        CalibratedThermalFrame lastRenderedFrame;

        byte[] FrameData;
        Queue<Bitmap> bmpQueue;

        public Form1()
        {
            InitializeComponent();

            DoubleBuffered = true;
            bmpQueue = new Queue<Bitmap>();

            var device = SeekThermal.Enumerate().FirstOrDefault();
            if(device == null)
            {
                MessageBox.Show("No Seek Thermal devices found.");
                return;
            }
            thermal = new SeekThermal(device);

            thermalThread = new Thread(ThermalThreadProc);
            thermalThread.IsBackground = true;
            thermalThread.Start();
        }

        void ThermalThreadProc()
        {
            while (!stopThread && thermal != null)
            {
                bool progress = false;
                lastFrame = thermal.GetFrameBlocking();

                if(lastFrame.IsCalibrationFrame)
                {
                    lastCalibrationFrame = lastFrame;
                }
                else
                {
                    if(lastCalibrationFrame != null && lastFrame.IsUsableFrame)
                    {
                        lastUsableFrame = lastFrame.ProcessFrame(lastCalibrationFrame);
                        progress = true;
                    }
                }


                //System.Diagnostics.Debug.Print("Start of data: " + string.Join(" ", lastFrame.RawData.Take(64).Select(b => b.ToString("x2")).ToArray()));
                //System.Diagnostics.Debug.Print("End of data: " + string.Join(" ", lastFrame.RawData.Reverse().Take(32).Reverse().Select(b => b.ToString("x2")).ToArray()));
                
                // Do stuff
                frameCount++;
                if(progress)
                {
                    Invalidate();
                }
            }
        }



        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            stopThread = true;
            if (thermal != null)
            {
                thermalThread.Join(500);
                thermal.Deinit();
            }
        }

        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            CalibratedThermalFrame data = lastUsableFrame;
            if (data == null) return;
            int y;
            if(data != lastRenderedFrame)
            {
                lastRenderedFrame = data;
                // Process new frame
                Bitmap bmp = new Bitmap(data.Width, data.Height);
                int c = 0;

                for (y = 0; y < 156; y++)
                {
                    for (int x = 0; x < 208; x++)
                    {
                        int v = data.PixelData[c++];

                        v = (v - data.MinValue) * 255 / (data.MaxValue - data.MinValue);
                        if (v < 0) v = 0;
                        if (v > 255) v = 255;

                        bmp.SetPixel(x, y, Color.FromArgb(v, v, v));
                    }
                }

                bmpQueue.Enqueue(bmp);
                if (bmpQueue.Count > 5) bmpQueue.Dequeue(); 
            }

            y = 10;
            foreach(Bitmap b in bmpQueue.Reverse())
            {
                e.Graphics.DrawImage(b, 10, y);
                y += b.Height + 10;
            }
        }
    }
}

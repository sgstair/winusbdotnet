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
            while (!stopThread)
            {
                byte[] data = thermal.GetFrameBlocking();

                //System.Diagnostics.Debug.Print("Start of data: " + string.Join(" ", data.Take(32).Select(b => b.ToString("x2")).ToArray()));
                System.Diagnostics.Debug.Print("End of data: " + string.Join(" ", data.Reverse().Take(32).Reverse().Select(b => b.ToString("x2")).ToArray()));
                
                // Do stuff
                frameCount++;
                FrameData = data;
                Invalidate();
            }
        }



        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            stopThread = true;
            thermalThread.Join(500);
            thermal.Deinit();
        }

        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            byte[] data = FrameData;
            if (data == null) return;
            Bitmap bmp = new Bitmap(208, 156);
            int c = 0;
            int y;
            for(y=0;y<156;y++)
            {
                for(int x=0;x<208;x++)
                {
                    int r = data[c++];
                    int g = data[c++];
                    bmp.SetPixel(x, y, Color.FromArgb(r,g,0));
                }
            }

            bmpQueue.Enqueue(bmp);
            if (bmpQueue.Count > 5) bmpQueue.Dequeue(); 

            y = 10;
            foreach(Bitmap b in bmpQueue.Reverse())
            {
                e.Graphics.DrawImage(b, 10, y);
                y += b.Height + 10;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace microphone
{
    public partial class UploadingForm : Form
    {
        public UploadingForm()
        {
            InitializeComponent();
        }

        public void SetValue(double Percentage)
        {
            int value = (int)(Percentage);
            if (value < 0) value = 0;
            if (value > 100) value = 100;
            progressBar1.Value = value;
        }
    }
}

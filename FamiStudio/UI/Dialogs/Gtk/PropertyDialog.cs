﻿using Gtk;
using System;
using System.Reflection;
using System.Resources;

namespace FamiStudio
{
    public class PropertyDialog : Window
    {
        private PropertyPage propertyPage = new PropertyPage();
        private System.Windows.Forms.DialogResult result = System.Windows.Forms.DialogResult.None;

        public  PropertyPage Properties => propertyPage;

        public PropertyDialog(System.Drawing.Point pt, int width, bool leftAlign = false) : base(WindowType.Toplevel)
        {
            var hbox = new HBox(false, 0);

            var suffix = GLTheme.DialogScaling >= 2.0f ? "@2x" : "";
            var buttonYes = new FlatButton(Gdk.Pixbuf.LoadFromResource($"FamiStudio.Resources.Yes{suffix}.png"));
            var buttonNo  = new FlatButton(Gdk.Pixbuf.LoadFromResource($"FamiStudio.Resources.No{suffix}.png"));

            buttonYes.Show();
            buttonYes.ButtonPressEvent += ButtonYes_ButtonPressEvent;
            buttonNo.Show();
            buttonNo.ButtonPressEvent += ButtonNo_ButtonPressEvent;

            hbox.PackStart(buttonYes, false, false, 0);
            hbox.PackStart(buttonNo, false, false, 0);
            hbox.Show();

            var align = new Alignment(1.0f, 0.5f, 0.0f, 0.0f);
            align.TopPadding = 5;
            align.Show();
            align.Add(hbox);

            var vbox = new VBox();
            vbox.PackStart(propertyPage, false, false, 0);
            vbox.PackStart(align, false, false, 0);
            vbox.Show();

            Add(vbox);

            propertyPage.PropertyWantsClose += propertyPage_PropertyWantsClose;
            propertyPage.Show();

            BorderWidth = 5;
            Resizable = false;
            Decorated = false;
            KeepAbove = true;
            Modal = true;
            WidthRequest = width;

            if (leftAlign)
                pt.X -= width;

            Move(pt.X, pt.Y);
        }

        private void ButtonNo_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            result = System.Windows.Forms.DialogResult.Cancel;
        }

        private void ButtonYes_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            result = System.Windows.Forms.DialogResult.OK;
        }

        private void propertyPage_PropertyWantsClose(int idx)
        {
            result = System.Windows.Forms.DialogResult.OK;
        }

        protected override bool OnKeyPressEvent(Gdk.EventKey evnt)
        {
            if (evnt.Key == Gdk.Key.Return)
            {
                result = System.Windows.Forms.DialogResult.OK;
            }
            else if (evnt.Key == Gdk.Key.Escape)
            {
                result = System.Windows.Forms.DialogResult.Cancel;
            }

            return base.OnKeyPressEvent(evnt);
        }

        public System.Windows.Forms.DialogResult ShowDialog()
        {
            Show();
#if FAMISTUDIO_MACOS
            MacUtils.SetNSWindowAlwayOnTop(MacUtils.NSWindowFromGdkWindow(GdkWindow.Handle));
#endif

            while (result == System.Windows.Forms.DialogResult.None)
                Application.RunIteration();

            Hide();
#if FAMISTUDIO_MACOS
            MacUtils.RestoreMainNSWindowFocus();
#endif

            return result;
        }
    }
}

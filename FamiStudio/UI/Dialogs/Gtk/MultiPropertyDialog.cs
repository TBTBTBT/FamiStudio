﻿using Gtk;
using System;
using System.Collections.Generic;

namespace FamiStudio
{
    public class MultiPropertyDialog : Window
    {
        class PropertyPageTab
        {
            public FlatButton button;
            public PropertyPage properties;
        }

        private int selectedIndex = 0;
        private List<PropertyPageTab> tabs = new List<PropertyPageTab>();
        private VBox buttonsVBox;
        private VBox propsVBox;
        private HBox mainHbox;
        private System.Windows.Forms.DialogResult result = System.Windows.Forms.DialogResult.None;

        public MultiPropertyDialog(int x, int y, int width, int height) : base(WindowType.Toplevel)
        {
            var buttonsHBox = new HBox(false, 0);

            var suffix = GLTheme.DialogScaling >= 2.0f ? "@2x" : "";
            var buttonYes = new FlatButton(Gdk.Pixbuf.LoadFromResource($"FamiStudio.Resources.Yes{suffix}.png"));
            var buttonNo  = new FlatButton(Gdk.Pixbuf.LoadFromResource($"FamiStudio.Resources.No{suffix}.png"));

            buttonYes.Show();
            buttonYes.ButtonPressEvent += ButtonYes_ButtonPressEvent;
            buttonNo.Show();
            buttonNo.ButtonPressEvent  += ButtonNo_ButtonPressEvent;

            buttonsHBox.PackStart(buttonYes, false, false, 0);
            buttonsHBox.PackStart(buttonNo, false, false, 0);
            buttonsHBox.HeightRequest = 40;
            buttonsHBox.Show();

            var buttonsAlign = new Alignment(1.0f, 0.5f, 0.0f, 0.0f);
            buttonsAlign.TopPadding = 5;
            buttonsAlign.Show();
            buttonsAlign.Add(buttonsHBox);

            var vbox  = new VBox();

            buttonsVBox = new VBox();
            buttonsVBox.Show();
            buttonsVBox.WidthRequest = 120;

            var buttonsVBoxPadding = new Alignment(0.0f, 0.0f, 0.0f, 0.0f);
            buttonsVBoxPadding.RightPadding = 5;
            buttonsVBoxPadding.Show();
            buttonsVBoxPadding.Add(buttonsVBox);

            propsVBox = new VBox();
            propsVBox.Show();
            propsVBox.HeightRequest = 250;

            mainHbox = new HBox();
            mainHbox.Show();
            mainHbox.PackStart(buttonsVBoxPadding, false, false, 0);
            mainHbox.PackStart(propsVBox, true, true, 0);

            vbox.Show();
            vbox.PackStart(mainHbox);
            vbox.PackStart(buttonsAlign, false, false, 0);

            Add(vbox);

            WidthRequest  = width;
            HeightRequest = height;

            BorderWidth = 5; 
            Resizable = false;
            Decorated = false;
            KeepAbove = true;
            Modal = true;

            Move(x, y);
        }

        public PropertyPage AddPropertyPage(string text, string image)
        {
            var suffix = GLTheme.DialogScaling >= 2.0f ? "@2x" : "";
            var pixbuf = Gdk.Pixbuf.LoadFromResource($"FamiStudio.Resources.{image}{suffix}.png");

            var page = new PropertyPage();
            page.Show();
            if (tabs.Count == 0)
            {
                propsVBox.PackStart(page, false, false, 0);
            }

            var tab = new PropertyPageTab();
            tab.button = AddButton(text, pixbuf);
            tab.properties = page;

            tabs.Add(tab);

            return page;
        }

        private FlatButton AddButton(string text, Gdk.Pixbuf image)
        {
            var btn = new FlatButton(image, text);

            btn.Show();
            btn.HeightRequest = 32;
            btn.Bold = tabs.Count == 0;
            btn.ButtonPressEvent += Btn_ButtonPressEvent;
            buttonsVBox.PackStart(btn, false, false, 0);

            return btn;
        }

        public PropertyPage GetPropertyPage(int idx)
        {
            return tabs[idx].properties;
        }

        public int SelectedIndex => selectedIndex;

        private void Btn_ButtonPressEvent(object sender, ButtonPressEventArgs args)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i].button == sender)
                {
                    selectedIndex = i;
                    tabs[i].button.Bold = true;
                    propsVBox.PackStart(tabs[i].properties, false, false, 0);
                }
                else
                {
                    tabs[i].button.Bold = false;
                    propsVBox.Remove(tabs[i].properties);
                }
            }
        }

        private void ButtonNo_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            result = System.Windows.Forms.DialogResult.Cancel;
        }

        private void ButtonYes_ButtonPressEvent(object o, ButtonPressEventArgs args)
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

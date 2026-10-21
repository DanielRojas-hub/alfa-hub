using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AlfaHub
{
    /// Solo para pruebas: ALFAHUB_TEST_SHOTS=<carpeta> hace que el hub recorra la página Perfiles solo
    /// (perfil nuevo con Alfa Sios sincronizado, sus pestañas, la ficha de una criatura, un item, el
    /// outfit en el ícono y compartir/importar), guarde cada pantalla en un PNG, apunte los errores en
    /// errores.txt y se cierre. Sirve para ver la interfaz sin pantalla (Wine) o en CI.
    sealed partial class HubForm
    {
        public void RunTestShots(string folder, Action quit)
        {
            Directory.CreateDirectory(folder);
            string log = Path.Combine(folder, "errores.txt");
            File.WriteAllText(log, "");
            Application.ThreadException += (s, e) => File.AppendAllText(log, e.Exception + Environment.NewLine + Environment.NewLine);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => File.AppendAllText(log, e.ExceptionObject + Environment.NewLine + Environment.NewLine);
            ClientSize = new Size(1000, 760);

            Profile profile = null;
            GameChar c = null;
            var steps = new List<(int Wait, Action Run)>
            {
                (1500, () => { ShowPage(HubPage.Profiles); }),
                (800, () => Shot(folder, "01-perfiles-vacio")),
                (300, () =>
                {
                    profile = Profiles.AddProfile();
                    c = profile.Chars[0];
                    Profiles.OpenProfile = profile.Id;
                    ShowPage(HubPage.Profiles);
                }),
                (800, () => Shot(folder, "02-perfil-nuevo")),
                (300, () =>
                {
                    c.Name = "Alfa Sios";
                    c.Kills["Demon"] = 2500; c.Kills["Rotworm"] = 500; c.Kills["Hydra"] = 30;
                    c.Charms["Curse"] = 2; c.Charms["Zap"] = 1; c.Charms["Overpower"] = 3; c.Charms["Vampiric Embrace"] = 2;
                    c.Hunt.Add(new HuntCreature { Name = "Crypt Fiend", PerHour = 874 });
                    c.Hunt.Add(new HuntCreature { Name = "Crypt Construct", PerHour = 532 });
                    c.Hunt.Add(new HuntCreature { Name = "Creepy Crawler", PerHour = 512 });
                    foreach (var (item, place, slot, detail, count) in new[]
                    {
                        ("Falcon Coif", "equipped", "helmet", "", 1), ("Falcon Plate", "equipped", "armor", "", 1), ("Amulet of Loss", "equipped", "amulet", "", 1),
                        ("Cobra Boots", "equipped", "boots", "", 1), ("Falcon Shield", "house", null, "Roshamuul", 1), ("Falcon Greaves", "house", null, "Roshamuul", 1),
                        ("Great Shield", "depot", null, "Thais", 1), ("Magic Plate Armor", "depot", null, "Edron", 1), ("Ring of Healing", "backpack", null, "", 5),
                    })
                        c.Items.Add(new OwnedItem { Item = item, Place = place, Slot = slot, Detail = detail, Count = count });
                    Profiles.OpenChar = c.Id;
                    charTab = CharTab.General;
                    ShowPage(HubPage.Profiles);   // la cabecera busca el nombre en tibia.com
                }),
                (25000, () => ShowPage(HubPage.Profiles)),
                (6000, () => Shot(folder, "03-char-general")),
                (300, () => Tab(CharTab.Experience)),
                (1500, () => Shot(folder, "04-char-experiencia")),
                (300, () => Tab(CharTab.Bestiary)),
                (15000, () => Shot(folder, "05-char-bestiary")),
                (300, () => { BestiaryCanvas.Mode = 1; Rebuild(); }),
                (6000, () => Shot(folder, "06-bestiary-single")),
                (300, () => { BestiaryCanvas.Mode = 2; Rebuild(); }),
                (6000, () => Shot(folder, "07-bestiary-group")),
                (300, () => { BestiaryCanvas.Mode = 0; Rebuild(); OpenPopover(new CreatureDetail(BestiaryCatalog.Find("Demon"), c, () => { }), new Size(380, 430)); }),
                (5000, () => ShotPopover(folder, "08-ficha-criatura")),
                (300, () => { ClosePopover(); Tab(CharTab.Charms); }),
                (8000, () => Shot(folder, "09-charms-optimizador")),
                (300, () => { CharmsCanvas.ShowTable = true; Rebuild(); }),
                (2000, () => Shot(folder, "10-charms-tabla")),
                (300, () => { CharmsCanvas.Mode = 1; Rebuild(); }),
                (6000, () => Shot(folder, "11-mis-charms")),
                (300, () => { CharmsCanvas.Mode = 0; Tab(CharTab.Inventory); }),
                (8000, () => Shot(folder, "12-inventario")),
                (300, () => OpenPopover(new ItemEditor(c.Items[4], c, new List<string>(), (slot, text) => ItemCatalog.For(slot).Take(30).Select(i => (i.Name, Slots.Title(i.Slot), i.Name))), new Size(330, 470))),
                (4000, () => ShotPopover(folder, "13-editor-item")),
                (300, () =>
                {
                    ClosePopover();
                    Outfit.SetDraft(OutfitConvert.ToConfig(new SharedOutfit { Outfit = 3, Addon1 = true, Addon2 = true }));
                    editingOutfit = true;
                    ShowPage(HubPage.Profiles);
                }),
                (10000, () => { Profiles.IconChar = c.Id; Rebuild(); }),
                (8000, () => Shot(folder, "14-outfit-del-char")),
                (300, () => { editingOutfit = false; ShowPage(HubPage.Profiles); }),
                (1500, () => Shot(folder, "15-char-con-outfit")),
                (300, () =>
                {
                    // Compartir e importar: el código vuelve como un perfil compartido.
                    var code = ProfileShare.Code(profile);
                    var back = ProfileShare.Decode(System.Text.Encoding.UTF8.GetBytes(code));
                    File.WriteAllText(Path.Combine(folder, "codigo.txt"), code);
                    if (back == null) File.AppendAllText(log, "ERROR: el código no se pudo leer" + Environment.NewLine);
                    else Profiles.Import(back);
                    Profiles.OpenProfile = null;
                    Profiles.OpenChar = null;
                    ShowPage(HubPage.Profiles);
                }),
                (1500, () => Shot(folder, "16-perfiles-importado")),
                (500, () => { Profiles.Save(); File.AppendAllText(log, "FIN" + Environment.NewLine); quit(); }),
            };
            int index = 0;
            var timer = new Timer { Interval = steps[0].Wait };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try { steps[index].Run(); }
                catch (Exception ex) { File.AppendAllText(log, $"Paso {index}: {ex}{Environment.NewLine}{Environment.NewLine}"); }
                index++;
                if (index < steps.Count) { timer.Interval = steps[index].Wait; timer.Start(); }
            };
            timer.Start();
        }

        Popover testPopover;

        void Tab(CharTab tab) { charTab = tab; Rebuild(); }

        void OpenPopover(Control content, Size size) => testPopover = Popover.Open(this, PointToScreen(new Point(300, 120)), content, size);

        void ClosePopover() { testPopover?.Close(); testPopover = null; }

        void Shot(string folder, string name)
        {
            using (var bitmap = Render(this, ClientSize)) bitmap.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
            // Y la página entera (con lo que queda fuera por el scroll).
            if (content.Controls.Count > 0 && content.Controls[0] is StackPanel page)
            {
                var old = page.Dock;
                var scroll = page.AutoScrollPosition;
                page.AutoScrollPosition = Point.Empty;
                page.Dock = DockStyle.None;
                page.AutoScroll = false;
                page.Height = Math.Min(6000, Math.Max(page.Height, page.AutoScrollMinSize.Height));
                page.PerformLayout();
                using (var bitmap = Render(page, page.Size)) bitmap.Save(Path.Combine(folder, name + "-entera.png"), ImageFormat.Png);
                page.AutoScroll = true;
                page.Dock = old;
                page.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
            }
        }

        void ShotPopover(string folder, string name)
        {
            if (testPopover == null) return;
            using (var bitmap = Render(testPopover, testPopover.ClientSize)) bitmap.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
        }

        /// Dibuja un control y sus hijos llamando a su propio pintado (DrawToBitmap no dibuja los
        /// controles pintados a mano en Wine). Los controles nativos (cuadros de texto, listas) quedan
        /// con su color de fondo.
        Bitmap Render(Control root, Size size)
        {
            var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(root.BackColor);
                PaintTree(g, root, Point.Empty, new Rectangle(Point.Empty, size));
            }
            return bitmap;
        }

        void PaintTree(Graphics g, Control control, Point offset, Rectangle clip)
        {
            var bounds = new Rectangle(offset, control.ClientSize);
            var visible = Rectangle.Intersect(bounds, clip);
            if (visible.IsEmpty || control.ClientSize.Width <= 0 || control.ClientSize.Height <= 0) return;
            // Cada control en su propia imagen (TextRenderer no respeta las transformaciones de Graphics).
            var local = new Rectangle(visible.X - offset.X, visible.Y - offset.Y, visible.Width, visible.Height);
            using (var layer = new Bitmap(control.ClientSize.Width, control.ClientSize.Height))
            {
                using (var lg = Graphics.FromImage(layer))
                using (var args = new PaintEventArgs(lg, local))
                {
                    lg.Clear(control.BackColor.A == 255 ? control.BackColor : Theme.Back);
                    if (!(control is TextBoxBase || control is ListBox))
                    {
                        try { InvokePaintBackground(control, args); InvokePaint(control, args); }
                        catch (Exception) { }
                    }
                    if (control is TextBoxBase box && box.Text.Length > 0)
                        TextRenderer.DrawText(lg, box.Text, box.Font, new Rectangle(Point.Empty, control.ClientSize), box.ForeColor,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
                var state = g.Save();
                g.SetClip(visible);
                g.DrawImageUnscaled(layer, offset);
                g.Restore(state);
            }
            for (int i = control.Controls.Count - 1; i >= 0; i--)
            {
                var child = control.Controls[i];
                if (!child.Visible) continue;
                PaintTree(g, child, new Point(offset.X + child.Left, offset.Y + child.Top), visible);
            }
        }
    }
}

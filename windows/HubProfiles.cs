using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AlfaHub
{
    // Página Perfiles: la lista de perfiles, cada perfil con sus chars y cada char con su outfit y cinco
    // pestañas: General y Experiencia (al estilo de guildstats.eu), Bestiary (como el Bestiary Tracker de
    // tibiaroute.com), Charms (con el optimizador como el de hakaimarket.com) e Inventario. Igual que en
    // macOS (macos/ProfilesPage.swift y macos/Profile*.swift).

    enum CharTab { General, Experience, Bestiary, Charms, Inventory }

    /// El outfit de un char (con las claves compartidas) ↔ el del Outfiter de Windows.
    static class OutfitConvert
    {
        public static OutfitConfig ToConfig(SharedOutfit o) => o == null ? new OutfitConfig() : new OutfitConfig
        {
            Outfit = o.Outfit, Female = o.Female, Addon1 = o.Addon1, Addon2 = o.Addon2, C1 = o.C1, C2 = o.C2, C3 = o.C3, C4 = o.C4,
            Facing = o.Facing, Mount = o.Mount, Mounted = o.Mounted, Mc1 = o.Mc1, Mc2 = o.Mc2, Mc3 = o.Mc3, Mc4 = o.Mc4, Animate = o.Animate,
        };

        public static SharedOutfit FromConfig(OutfitConfig c) => c == null || c.IsOriginal ? null : new SharedOutfit
        {
            Outfit = c.Outfit, Female = c.Female, Addon1 = c.Addon1, Addon2 = c.Addon2, C1 = c.C1, C2 = c.C2, C3 = c.C3, C4 = c.C4,
            Facing = c.Facing, Mount = c.Mount, Mounted = c.Mounted, Mc1 = c.Mc1, Mc2 = c.Mc2, Mc3 = c.Mc3, Mc4 = c.Mc4, Animate = c.Animate,
        };
    }

    sealed partial class HubForm
    {
        /// Perfiles, chars y el char del ícono de la app.
        public readonly ProfileStore Profiles = new ProfileStore();
        CharTab charTab;
        bool editingOutfit;

        Control BuildProfilesPage()
        {
            var profile = Profiles.Find(Profiles.OpenProfile);
            var c = Profiles.FindChar(profile, Profiles.OpenChar);
            if (c != null) return editingOutfit ? BuildCharOutfitPage(profile, c) : BuildCharPage(profile, c);
            editingOutfit = false;
            return profile != null ? BuildProfilePage(profile) : BuildProfileListPage();
        }

        static RoundButton AccentButton(string text, Action click)
        {
            var b = new RoundButton { Text = text, FillColor = Theme.Blue, Pill = true, Height = 34, BackColor = Theme.Back, Font = new Font("Segoe UI Semibold", 10f) };
            b.FitText(16);
            b.Click += (s, e) => click();
            return b;
        }

        FormRow BackRow(string text, Action back)
        {
            var button = new RoundButton { Text = "‹  " + text, ForeColor = Theme.Blue, FillColor = Theme.Back, BackColor = Theme.Back, Height = 26 };
            button.FitText(2);
            button.Click += (s, e) => back();
            return new FormRow(null, button, RowLayout.Leading, 28) { BackColor = Theme.Back, Margin = new Padding(-12, 0, 0, 0) };
        }

        static InlineGroup Actions(params Control[] buttons)
        {
            var group = new InlineGroup(buttons) { BackColor = Theme.Back, Height = 34, Gap = 8 };
            Action fit = () => { group.Width = group.PreferredWidth; group.Parent?.PerformLayout(); };
            foreach (var b in buttons) b.SizeChanged += (s, e) => fit();
            fit();
            return group;
        }

        // ---------- Lista de perfiles ----------

        Control BuildProfileListPage()
        {
            var import = SmallButton("⤓  Importar", null);
            import.Click += (s, e) =>
            {
                var menu = DarkMenu.Create();
                menu.Items.Add("Pegar código…", null, (a, b) => PasteCode());
                menu.Items.Add("Abrir archivo ." + ProfileShare.FileExtension + "…", null, (a, b) => ImportFile());
                menu.Show(import, new Point(0, import.Height + 4));
            };
            var add = AccentButton("＋  Nuevo perfil", () => { var p = Profiles.AddProfile(); Profiles.OpenProfile = p.Id; Rebuild(); });
            var items = new List<Control> { new PageHeader("Perfiles", "Tus chars, sus datos de tibia.com, Bestiary, charms e inventario; compártelos", Actions(import, add)) };
            if (Profiles.Profiles.Count == 0)
                items.Add(new EmptyCanvas("Todavía no hay perfiles",
                    "Un perfil junta tus chars: sus datos de tibia.com, su experiencia, su Bestiary y sus charms, y qué items tiene cada uno y dónde están. Puedes compartirlo con un código o un archivo."));
            foreach (var profile in Profiles.Profiles)
            {
                var p = profile;
                items.Add(new ProfileRow(p, Outfit)
                {
                    Margin = new Padding(0, 0, 0, 8),
                    Open = () => { Profiles.OpenProfile = p.Id; Profiles.OpenChar = null; Rebuild(); },
                    Share = at => ShareMenu(p).Show(at),
                    More = at =>
                    {
                        var menu = DarkMenu.Create();
                        menu.Items.Add("Duplicar", null, (a, b) => { Profiles.Duplicate(p); Rebuild(); });
                        menu.Items.Add("Eliminar", null, (a, b) =>
                        {
                            if (Confirm($"¿Eliminar “{p.Name}”?", "Se borran sus chars, items y Bestiary. No se puede deshacer.")) { Profiles.Remove(p); Rebuild(); }
                        }).ForeColor = Color.FromArgb(255, 105, 97);
                        menu.Show(at);
                    },
                });
            }
            return Page(items.ToArray());
        }

        bool Confirm(string message, string detail) =>
            MessageBox.Show(this, detail, message, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        ContextMenuStrip ShareMenu(Profile p)
        {
            var menu = DarkMenu.Create();
            menu.Items.Add("Copiar código para compartir", null, (a, b) =>
            {
                Clipboard.SetText(ProfileShare.Code(p));
                Notice($"Código de “{p.Name}” copiado. Pégalo en un chat: quien lo reciba lo abre en Perfiles → Importar.");
            });
            menu.Items.Add("Guardar archivo ." + ProfileShare.FileExtension + "…", null, (a, b) =>
            {
                using (var dialog = new SaveFileDialog { Filter = "Perfil de Alfa Hub (*.alfahub)|*.alfahub", FileName = p.Name + ".alfahub" })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        File.WriteAllBytes(dialog.FileName, ProfileShare.Json(p));
                        Notice($"Guardado “{Path.GetFileName(dialog.FileName)}”. Envía el archivo a quien quieras.");
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Notice("No se pudo guardar: " + e.Message); }
                }
            });
            return menu;
        }

        void ImportFile()
        {
            using (var dialog = new OpenFileDialog { Filter = "Perfil de Alfa Hub (*.alfahub;*.json;*.txt)|*.alfahub;*.json;*.txt|Todos los archivos|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                byte[] data;
                try { data = File.ReadAllBytes(dialog.FileName); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Notice("No se pudo leer el archivo: " + e.Message); return; }
                FinishImport(ProfileShare.Decode(data));
            }
        }

        void PasteCode()
        {
            var panel = new PastePanel();
            var pop = Popover.Open(this, PointToScreen(new Point(Width / 2 - 240, 120)), panel, new Size(480, 260));
            panel.Done += profile =>
            {
                if (profile == null) { panel.ShowError("No es un perfil de Alfa Hub (o el código está incompleto)."); return; }
                pop.Close();
                FinishImport(profile);
            };
        }

        void FinishImport(Profile shared)
        {
            if (shared == null) { Notice("No es un perfil de Alfa Hub (o el código está incompleto)."); return; }
            var (profile, updated) = Profiles.Import(shared);
            Profiles.OpenProfile = profile.Id;
            Profiles.OpenChar = null;
            ShowPage(HubPage.Profiles);
            Notice(updated ? $"Se actualizó “{profile.Name}” con la versión nueva." : $"Perfil “{profile.Name}” importado.");
        }

        /// Aviso pasajero al pie del hub.
        void Notice(string text)
        {
            var toast = new Form
            {
                FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                BackColor = Color.FromArgb(52, 52, 56), ForeColor = Color.White, Font = new Font("Segoe UI", 9.75f), TopMost = true,
            };
            var size = TextRenderer.MeasureText(text, toast.Font, new Size(520, 0), TextFormatFlags.WordBreak);
            toast.ClientSize = new Size(Math.Min(560, size.Width + 32), size.Height + 22);
            toast.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10), ForeColor = Color.White });
            var area = RectangleToScreen(content.Bounds);
            toast.Location = new Point(area.Left + (area.Width - toast.Width) / 2, area.Bottom - toast.Height - 20);
            var timer = new Timer { Interval = 5000 };
            timer.Tick += (s, e) => { timer.Dispose(); toast.Close(); };
            toast.Click += (s, e) => toast.Close();
            toast.Show(this);
            timer.Start();
        }

        // ---------- Un perfil ----------

        Control BuildProfilePage(Profile profile)
        {
            var name = new InputField("Nombre del perfil", profile.Name) { Borderless = true, FieldColor = Theme.Back, Height = 40 };
            name.TextFont = new Font("Segoe UI", 17f, FontStyle.Bold);
            name.Changed += text => { profile.Name = text; Profiles.Touch(); };
            var share = SmallButton("⤴  Compartir", null);
            share.Click += (s, e) => ShareMenu(profile).Show(share, new Point(0, share.Height + 4));
            var add = AccentButton("＋  Añadir char", () =>
            {
                var c = new GameChar();
                profile.Chars.Add(c);
                Profiles.Touch();
                Profiles.OpenChar = c.Id;
                Rebuild();
            });
            var items = new List<Control>
            {
                BackRow("Volver a Perfiles", () => { Profiles.OpenProfile = null; Rebuild(); }),
                new TitleRow(name, Actions(share, add)),
            };
            if (profile.ImportedAt != null)
                items.Add(new NoteLabel($"Perfil compartido, importado el {Format.Long(Ids.Parse(profile.ImportedAt) ?? DateTime.UtcNow, true)}. Si te pasan una versión nueva, impórtala y se actualiza esta copia.") { Margin = new Padding(4, 0, 4, 8) });
            if (profile.Chars.Count == 0)
                items.Add(new EmptyCanvas("Sin chars", "Añade los chars de este perfil para ver sus datos de tibia.com, su Bestiary, sus charms y sus items."));
            items.Add(new CharGrid(profile, Outfit)
            {
                Margin = new Padding(0, 8, 0, 8),
                Open = c => { Profiles.OpenChar = c.Id; Rebuild(); },
                More = (c, at) =>
                {
                    var menu = DarkMenu.Create();
                    menu.Items.Add("Duplicar", null, (a, b) =>
                    {
                        var copy = ProfileFiles.Deserialize<GameChar>(ProfileFiles.Serialize(c));
                        copy.Id = Ids.New();
                        copy.Name += " (copia)";
                        profile.Chars.Add(copy);
                        Profiles.Touch();
                        Rebuild();
                    });
                    menu.Items.Add("Eliminar", null, (a, b) =>
                    {
                        if (Confirm($"¿Eliminar “{c.Name}”?", "Se borran sus items y su Bestiary.")) { profile.Chars.Remove(c); Profiles.Touch(); Rebuild(); }
                    }).ForeColor = Color.FromArgb(255, 105, 97);
                    menu.Show(at);
                },
            });
            return Page(items.ToArray());
        }

        // ---------- Un char ----------

        Control BuildCharPage(Profile profile, GameChar c)
        {
            var header = new CharHeader(c, Profiles, Outfit) { Margin = new Padding(0, 4, 0, 6) };
            header.EditOutfit = () =>
            {
                Outfit.SetDraft(OutfitConvert.ToConfig(c.Outfit));
                editingOutfit = true;
                Rebuild();
            };
            header.Synced = Rebuild;
            var tabs = new CharTabs(charTab) { Margin = new Padding(0, 6, 0, 12) };
            tabs.Pick = tab => { charTab = tab; Rebuild(); };
            Control body;
            switch (charTab)
            {
                case CharTab.Experience: body = new ExperienceCanvas(c); break;
                case CharTab.Bestiary: body = new BestiaryCanvas(c, Profiles); break;
                case CharTab.Charms: body = new CharmsCanvas(c, Profiles); break;
                case CharTab.Inventory: body = new InventoryCanvas(c, profile, Profiles); break;
                default:
                    body = new GeneralCanvas(c, profile)
                    {
                        Add = other =>
                        {
                            profile.Chars.Add(new GameChar { Name = other.Name, World = other.World });
                            Profiles.Touch();
                            Notice($"“{other.Name}” añadido a {profile.Name}.");
                            Rebuild();
                        },
                    };
                    break;
            }
            return Page(BackRow("Volver a " + profile.Name, () => { Profiles.OpenChar = null; Rebuild(); }), header, tabs, body);
        }

        Control BuildCharOutfitPage(Profile profile, GameChar c)
        {
            string name = c.Name.Length == 0 ? "este char" : c.Name;
            var icon = new ToggleSwitch { Checked = Profiles.IconChar == c.Id, Text = "Mostrar en el ícono de Alfa Hub" };
            icon.CheckedChanged += (s, e) => Profiles.IconChar = icon.Checked ? c.Id : null;
            var caption = new Caption("Mostrar en el ícono de Alfa Hub") { BackColor = Theme.Back, ForeColor = Color.White };
            caption.Cursor = Cursors.Hand;
            caption.Click += (s, e) => icon.Checked = !icon.Checked;  // como en las filas de ajustes: el texto también cambia el interruptor
            var editor = new OutfitEditor(Outfit) { Margin = new Padding(0, 4, 0, 8) };
            // Los cambios se guardan solos en el char.
            Action save = () =>
            {
                var shared = OutfitConvert.FromConfig(Outfit.Draft);
                c.Outfit = shared;
                Profiles.Touch();
            };
            Outfit.Changed += save;
            editor.Disposed += (s, e) => Outfit.Changed -= save;
            return Page(
                BackRow("Volver a " + name, () => { editingOutfit = false; Rebuild(); }),
                new PageHeader("Outfit de " + name, "Como en el Outfiter de TibiaWiki; los cambios se guardan solos", Actions(caption, icon)),
                editor,
                new NoteLabel("Con “Mostrar en el ícono de Alfa Hub” este personaje pasa al ícono de la ventana, a la bandeja y a la barra lateral; solo un char a la vez. Los sprites vienen del Outfiter de TibiaWiki: se descargan la primera vez que eliges cada outfit o montura y luego funcionan sin conexión. El ícono del archivo AlfaHub.exe sigue siendo el original."));
        }
    }

    /// Un campo de título grande a la izquierda y botones a la derecha.
    sealed class TitleRow : PaintedControl, IStackItem
    {
        readonly Control title, actions;

        public TitleRow(Control title, Control actions)
        {
            this.title = title;
            this.actions = actions;
            BackColor = Theme.Back;
            Margin = new Padding(0, 4, 0, 8);
            Controls.Add(title);
            Controls.Add(actions);
        }

        public int HeightFor(int width) => (int)(44 * K);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            actions.Location = new Point(Width - actions.Width, (Height - actions.Height) / 2);
            title.SetBounds(0, (Height - title.Height) / 2, Math.Max(100, actions.Left - (int)(12 * K)), title.Height);
        }

        protected override void OnPaint(PaintEventArgs e) => Begin(e);
    }

    /// Tarjeta vacía: ícono, título y explicación.
    sealed class EmptyCanvas : HitCanvas
    {
        readonly string title, text;

        public EmptyCanvas(string title, string text) { this.title = title; this.text = text; Margin = new Padding(0, 4, 0, 8); }

        public override int HeightFor(int width) => (int)S(70) + MeasureWrapped(text, Fonts.Get(9f), width - (int)S(80));

        protected override void Draw(Graphics g, Rectangle visible)
        {
            Card(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1));
            Write(g, title, Fonts.Semi(10.5f), Color.White, new RectangleF(0, S(18), Width, S(22)), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            Write(g, text, Fonts.Get(9f), Theme.Muted, new RectangleF(S(40), S(46), Width - S(80), Height - S(50)),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }
    }

    /// Pegar un código compartido.
    sealed class PastePanel : PaintedControl
    {
        readonly TextBox box = new TextBox
        {
            Multiline = true, BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Color.White,
            Font = new Font("Consolas", 8.5f), ScrollBars = ScrollBars.Vertical, AccessibleName = "Código del perfil",
        };
        readonly RoundButton import = new RoundButton { Text = "Importar", FillColor = Theme.Blue, Height = 30 };
        readonly RoundButton cancel = new RoundButton { Text = "Cancelar", FillColor = Theme.ControlFill, Height = 30 };
        string error;

        public event Action<Profile> Done;

        public PastePanel()
        {
            BackColor = Theme.Row;
            import.FitText(16);
            cancel.FitText(16);
            try { if (Clipboard.ContainsText() && Clipboard.GetText().Trim().StartsWith(ProfileShare.Prefix)) box.Text = Clipboard.GetText().Trim(); }
            catch (System.Runtime.InteropServices.ExternalException) { }
            import.Click += (s, e) => Done?.Invoke(ProfileShare.Decode(Encoding.UTF8.GetBytes(box.Text)));
            cancel.Click += (s, e) => FindForm()?.Close();
            Controls.Add(box);
            Controls.Add(import);
            Controls.Add(cancel);
        }

        public void ShowError(string text) { error = text; Invalidate(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = (int)(16 * K);
            box.SetBounds(pad, (int)(66 * K), Width - pad * 2, Height - (int)(66 * K) - (int)(70 * K));
            import.Location = new Point(Width - pad - import.Width, Height - pad - import.Height);
            cancel.Location = new Point(import.Left - (int)(8 * K) - cancel.Width, import.Top);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            TextRenderer.DrawText(g, "Importar perfil", new Font("Segoe UI", 13f, FontStyle.Bold), new Point((int)(16 * k), (int)(12 * k)), Color.White, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, $"Pega el código que te compartieron (empieza por {ProfileShare.Prefix}). Si ya tenías ese perfil, se actualiza.",
                Font, new Rectangle((int)(16 * k), (int)(40 * k), Width - (int)(32 * k), (int)(24 * k)), Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if (error != null)
                TextRenderer.DrawText(g, error, Font, new Rectangle((int)(16 * k), Height - (int)(44 * k), Width - (int)(220 * k), (int)(24 * k)),
                    Palette.Red, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// Fila de un perfil: los outfits de sus primeros chars, nombre, resumen, Compartir y ⋮.
    sealed class ProfileRow : HitCanvas
    {
        readonly Profile profile;
        readonly OutfitState outfit;
        public Action Open;
        public Action<Point> Share, More;

        public ProfileRow(Profile profile, OutfitState outfit)
        {
            this.profile = profile;
            this.outfit = outfit;
            outfit.Changed += Invalidate;
            Text = "Abrir " + profile.Name;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) outfit.Changed -= Invalidate;
            base.Dispose(disposing);
        }

        public override int HeightFor(int width) => (int)S(66);

        protected override void Draw(Graphics g, Rectangle visible)
        {
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Card(g, r, Hovered(r) ? Color.FromArgb(46, 46, 50) : (Color?)null, radius: 10);
            Hot(r, Open);
            float x = S(12);
            var chars = profile.Chars.Take(3).ToList();
            if (chars.Count == 0) chars.Add(null);
            foreach (var c in chars)
            {
                Avatar.Draw(g, outfit, c?.Outfit, new RectangleF(x, S(11), S(44), S(44)), K, true);
                x += S(30);
            }
            x += S(26);
            var nameFont = Fonts.Semi(10.5f);
            Write(g, profile.Name, nameFont, Color.White, new RectangleF(x, S(13), Width - x - S(200), S(22)));
            if (profile.ImportedAt != null)
                Chip(g, "COMPARTIDO", Theme.Blue, x + Math.Min(Measure(profile.Name, nameFont).Width, Width - x - S(200)) + S(8), S(16));
            string summary = string.Join(" · ", new[] { profile.Summary }.Concat(profile.Chars.Take(4).Select(c => c.Name.Length == 0 ? "Sin nombre" : c.Name)));
            Write(g, summary, Fonts.Get(8.5f), Theme.Muted, new RectangleF(x, S(37), Width - x - S(200), S(18)));
            float right = Width - S(12);
            var more = new RectangleF(right - S(30), (Height - S(30)) / 2, S(30), S(30));
            if (Hovered(more)) Fill(g, more, S(15), Color.FromArgb(30, 255, 255, 255));
            CenterText(g, "⋮", Fonts.Bold(12f), Color.White, more);
            Hot(more, null, "Más opciones", More);
            float w = ButtonWidth("⤴  Compartir");
            Button(g, "⤴  Compartir", more.X - S(8) - w, (Height - S(28)) / 2, null);
            hots[hots.Count - 1] = new Hot { Rect = hots[hots.Count - 1].Rect, Menu = Share, Tip = "Copiar código o guardar archivo" };
        }
    }

    /// El personaje de un char: su outfit (miniatura del Outfiter) o, sin outfit, el original de Alfa Hub.
    static class Avatar
    {
        public static void Draw(Graphics g, OutfitState state, SharedOutfit outfit, RectangleF box, float k, bool ring = false)
        {
            using (var path = CounterRenderer.RoundedRect(box, 9 * k))
            using (var brush = new SolidBrush(Color.FromArgb(51, 61, 41))) g.FillPath(brush, path);
            if (ring)
                using (var path = CounterRenderer.RoundedRect(box, 9 * k))
                using (var pen = new Pen(Theme.Back, 2 * k)) g.DrawPath(pen, path);
            var image = outfit == null ? Resources.Character : state.Thumbnail(OutfitConvert.ToConfig(outfit));
            if (image == null) return;
            float inner = box.Width - 6 * k;
            float s = Math.Min(2 * k, Math.Min(inner / image.Width, inner / image.Height));
            float w = image.Width * s, h = image.Height * s;
            Resources.DrawPixelArt(g, image, new RectangleF(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h));
        }
    }

    /// Tarjetas de los chars de un perfil (en columnas): outfit, nombre, vocación · nivel · mundo, items y Bestiary.
    sealed class CharGrid : HitCanvas
    {
        readonly Profile profile;
        readonly OutfitState outfit;
        public Action<GameChar> Open;
        public Action<GameChar, Point> More;

        public CharGrid(Profile profile, OutfitState outfit)
        {
            Text = "Chars del perfil";  // nombre para UI Automation (lo usa el script de capturas)
            this.profile = profile;
            this.outfit = outfit;
            outfit.Changed += Invalidate;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) outfit.Changed -= Invalidate;
            base.Dispose(disposing);
        }

        int Columns(int width) => Math.Max(1, (int)((width + S(12)) / (S(290) + S(12))));

        public override int HeightFor(int width)
        {
            int rows = (profile.Chars.Count + Columns(width) - 1) / Columns(width);
            return rows == 0 ? 0 : (int)(rows * S(100) + (rows - 1) * S(12));
        }

        protected override void Draw(Graphics g, Rectangle visible)
        {
            int columns = Columns(Width);
            float w = (Width - (columns - 1) * S(12)) / columns, h = S(100);
            int total = BestiaryCatalog.Creatures.Count;
            for (int i = 0; i < profile.Chars.Count; i++)
            {
                var c = profile.Chars[i];
                var r = new RectangleF((i % columns) * (w + S(12)) + 0.5f, (i / columns) * (h + S(12)) + 0.5f, w - 1, h - 1);
                Card(g, r, Hovered(r) ? Color.FromArgb(46, 46, 50) : (Color?)null);
                Hot(r, () => Open(c), "Abrir " + (c.Name.Length == 0 ? "char" : c.Name));
                Avatar.Draw(g, outfit, c.Outfit, new RectangleF(r.X + S(12), r.Y + S(12), S(64), S(64)), K);
                float x = r.X + S(88), tw = r.Right - x - S(40);
                Write(g, c.Name.Length == 0 ? "Sin nombre" : c.Name, Fonts.Bold(11f), Color.White, new RectangleF(x, r.Y + S(12), tw, S(22)));
                Write(g, c.Summary, Fonts.Get(8.25f), Theme.Muted, new RectangleF(x, r.Y + S(36), tw, S(16)));
                int done = c.CompletedCount;
                Write(g, $"{c.Items.Count} {(c.Items.Count == 1 ? "item" : "items")} · Bestiary {done}/{total}", Fonts.Semi(8.25f),
                    Color.FromArgb(200, 200, 205), new RectangleF(x, r.Y + S(58), tw, S(16)));
                Bar(g, new RectangleF(x, r.Y + S(78), tw, S(4)), total == 0 ? 0 : (double)done / total, Theme.Green);
                var more = new RectangleF(r.Right - S(36), r.Y + S(8), S(30), S(30));
                if (Hovered(more)) Fill(g, more, S(15), Color.FromArgb(30, 255, 255, 255));
                CenterText(g, "⋮", Fonts.Bold(12f), Color.White, more);
                Hot(more, null, "Más opciones", at => More(c, at));
            }
        }
    }

    /// Cabecera de un char: outfit (clic para editarlo; en la esquina, mostrarlo en el ícono de Alfa
    /// Hub), nombre (al dejar de escribirlo se busca en tibia.com), vocación · mundo · gremio con el estado
    /// de la sincronización y, a la derecha, nivel, Bestiary y charm points.
    sealed class CharHeader : HitCanvas
    {
        readonly GameChar c;
        readonly ProfileStore store;
        readonly OutfitState outfit;
        readonly InputField name;
        readonly RoundButton vocation = new RoundButton { Height = 28, FillColor = Theme.Field, BorderColor = Color.FromArgb(40, 255, 255, 255) };
        readonly NumberField level = new NumberField { Minimum = 1, Maximum = 5000, Width = 58, Height = 28 };
        readonly InputField world = new InputField("Mundo") { Width = 110 };
        readonly Timer debounce = new Timer { Interval = 1200 };
        bool looking;
        string error;
        public Action EditOutfit;
        /// Llegaron datos nuevos de tibia.com (para volver a dibujar la página).
        public Action Synced;

        public CharHeader(GameChar c, ProfileStore store, OutfitState outfit)
        {
            Text = "Cabecera del char";  // nombre para UI Automation (lo usa el script de capturas)
            this.c = c;
            this.store = store;
            this.outfit = outfit;
            outfit.Changed += Invalidate;
            name = new InputField("Nombre del char en Tibia", c.Name) { Borderless = true, FieldColor = Theme.Back, Height = 36 };
            name.TextFont = new Font("Segoe UI", 16f, FontStyle.Bold);
            name.Changed += text =>
            {
                c.Name = text;
                store.Touch();
                error = null;
                debounce.Stop();
                if (text.Trim().Length > 0) debounce.Start();
                Invalidate();
            };
            debounce.Tick += (s, e) => { debounce.Stop(); Lookup(); };
            vocation.Text = Vocations.Title(c.Vocation) + "  ▾";
            vocation.FitText(10);
            vocation.Click += (s, e) =>
            {
                var menu = DarkMenu.Create();
                foreach (var v in Vocations.All)
                {
                    string value = v;
                    menu.Items.Add(Vocations.Title(v), null, (a, b) => { c.Vocation = value; vocation.Text = Vocations.Title(value) + "  ▾"; store.Touch(); });
                }
                menu.Show(vocation, new Point(0, vocation.Height + 4));
            };
            level.Value = c.Level;
            level.ValueCommitted += (s, e) => { c.Level = Math.Max(1, level.Value); store.Touch(); Invalidate(); };
            world.Value = c.World;
            world.Changed += text => { c.World = text; store.Touch(); };
            Controls.Add(name);
            Controls.Add(vocation);
            Controls.Add(level);
            Controls.Add(world);
            // Al abrir el char se vuelve a sincronizar si pasó más de una hora.
            if (c.Name.Trim().Length > 0 && (!c.IsSynced || (DateTime.UtcNow - c.Tibia.Synced).TotalHours >= 1))
                HandleCreated += (s, e) => Lookup();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { outfit.Changed -= Invalidate; debounce.Dispose(); }
            base.Dispose(disposing);
        }

        void Lookup()
        {
            string query = c.Name.Trim();
            if (query.Length == 0 || looking) return;
            looking = true;
            error = null;
            Invalidate();
            store.Lookup(query, info =>
            {
                looking = false;
                if (IsDisposed) return;
                // Si mientras tanto se cambió el nombre, esta respuesta ya no vale.
                if (!string.Equals(c.Name.Trim(), query, StringComparison.OrdinalIgnoreCase)) { Invalidate(); return; }
                if (info == null) { error = $"“{query}” no existe en tibia.com (o TibiaData no responde)"; Invalidate(); return; }
                c.Apply(info);
                store.Touch();
                Synced?.Invoke();
            });
        }

        public override int HeightFor(int width) => (int)S(86);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float x = S(86);
            name.SetBounds((int)x, (int)S(4), Math.Max(120, (int)(Width - x - S(330))), (int)S(36));
            bool manual = !c.IsSynced;
            vocation.Visible = level.Visible = world.Visible = manual;
            if (manual)
            {
                int y = (int)S(44);
                vocation.Location = new Point((int)x, y);
                level.Location = new Point(vocation.Right + (int)S(8), y);
                world.SetBounds(level.Right + (int)S(8), y, world.Width, (int)S(28));
            }
        }

        protected override void Draw(Graphics g, Rectangle visible)
        {
            bool isIcon = store.IconChar == c.Id;
            // Outfit
            var box = new RectangleF(S(2), S(8), S(70), S(70));
            Avatar.Draw(g, outfit, c.Outfit, box, K);
            bool hover = Hovered(box);
            var strip = new RectangleF(box.X, box.Bottom - S(17), box.Width, S(17));
            using (var path = CounterRenderer.RoundedRect(box, S(9)))
            {
                var clip = g.Clip;
                g.SetClip(path, System.Drawing.Drawing2D.CombineMode.Intersect);
                using (var brush = new SolidBrush(Color.FromArgb(hover ? 190 : 130, 0, 0, 0))) g.FillRectangle(brush, strip);
                g.Clip = clip;
            }
            CenterText(g, hover ? "✎ Editar outfit" : "✎ Outfit", Fonts.Bold(7f), Color.White, strip);
            if (hover) Stroke(g, box, S(9), Theme.Blue, S(2));
            Hot(box, EditOutfit, "Editar el outfit de este char");
            var badge = new RectangleF(box.Right - S(13), box.Y - S(9), S(22), S(22));
            using (var brush = new SolidBrush(Theme.Back)) g.FillEllipse(brush, RectangleF.Inflate(badge, S(2), S(2)));
            using (var brush = new SolidBrush(isIcon ? Theme.Blue : Color.FromArgb(46, 46, 50))) g.FillEllipse(brush, badge);
            using (var icons = new Font("Segoe MDL2 Assets", 8f)) CenterText(g, isIcon ? "" : "", icons, Color.White, badge);
            Hot(badge, () => { store.IconChar = isIcon ? null : c.Id; Invalidate(); },
                isIcon ? "Sale en el ícono de Alfa Hub (ventana, bandeja y barra lateral). Clic para quitarlo."
                       : "Mostrar este char en el ícono de Alfa Hub (ventana, bandeja y barra lateral)");

            float x = S(86);
            if (isIcon) Chip(g, "ÍCONO", Theme.Blue, x + Math.Min(Measure(c.Name, Fonts.Bold(16f)).Width + S(14), Width - x - S(380)), S(16));
            if (c.IsSynced)
            {
                var info = c.Tibia;
                string line = string.Join(" · ", new[] { info.Vocation, info.World, info.Guild }.Where(t => !string.IsNullOrEmpty(t)));
                var font = Fonts.Semi(9.75f);
                Write(g, line, font, Color.FromArgb(220, 220, 225), new RectangleF(x, S(46), Width - x - S(330), S(20)));
                float sx = x + Math.Min(Measure(line, font).Width + S(12), Width - x - S(330));
                string status = (looking ? "⟳ " : "✓ ") + "tibia.com " + Format.Relative(info.Synced);
                var statusRect = new RectangleF(sx, S(47), Measure(status, Fonts.Get(8.25f)).Width + 4, S(18));
                Write(g, status, Fonts.Get(8.25f), looking ? Theme.Muted : Theme.Green, statusRect);
                Hot(statusRect, Lookup, "Sincronizado con tibia.com; clic para volver a sincronizar");
            }
            else
            {
                string status = looking ? "Buscando en tibia.com…" : error ?? "Escribe el nombre del char y se sincroniza con tibia.com";
                var r = new RectangleF(x, S(74), Width - x - S(330), S(16));
                Write(g, status, Fonts.Get(8.25f), error != null ? Theme.Amber : Theme.Muted, r);
                if (error != null && !looking)
                {
                    float w = Measure(status, Fonts.Get(8.25f)).Width;
                    var retry = new RectangleF(x + Math.Min(w + S(8), r.Width - S(70)), r.Y, S(70), r.Height);
                    Write(g, "Reintentar", Fonts.Semi(8.25f), Theme.Blue, retry);
                    Hot(retry, Lookup);
                }
            }
            // Recuadros
            float right = Width;
            foreach (var (label, value, tint) in new[]
            {
                ("CHARM POINTS", N(c.CharmPoints), Palette.Purple),
                ("BESTIARY", $"{c.CompletedCount}/{BestiaryCatalog.Creatures.Count}", Theme.Amber),
                ("NIVEL", N(c.Level), Theme.Green),
            })
            {
                float w = Math.Max(Measure(label, Fonts.Semi(7f)).Width, Measure(value, Fonts.Bold(12f)).Width) + S(24);
                var r = new RectangleF(right - w, S(18), w, S(48));
                Card(g, r, radius: 9);
                TextAt(g, label, Fonts.Semi(7f), Theme.Muted, r.X + S(12), r.Y + S(7));
                TextAt(g, value, Fonts.Bold(12f), tint, r.X + S(12), r.Y + S(21));
                right = r.X - S(8);
            }
        }
    }

    /// Pestañas del char con una raya azul debajo de la elegida (como guildstats.eu). Cada pestaña es un
    /// botón con su nombre (así las encuentra el script de capturas).
    sealed class CharTabs : PaintedControl, IStackItem
    {
        public Action<CharTab> Pick;
        static readonly (CharTab Tab, string Title, string Glyph)[] Tabs =
        {
            (CharTab.General, "General", "\uE77B"), (CharTab.Experience, "Experiencia", "\uE9D2"), (CharTab.Bestiary, "Bestiary", "\uE7BE"),
            (CharTab.Charms, "Charms", "\uE734"), (CharTab.Inventory, "Inventario", "\uE7B8"),
        };

        public CharTabs(CharTab selected)
        {
            BackColor = Theme.Back;
            foreach (var (tab, title, glyph) in Tabs)
            {
                var t = tab;
                var button = new TabButton(title, glyph, tab == selected) { BackColor = Theme.Back };
                button.Click += (s, e) => Pick?.Invoke(t);
                Controls.Add(button);
            }
        }

        public int HeightFor(int width) => (int)(38 * K);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = 0;
            foreach (Control c in Controls) { c.SetBounds(x, 0, c.Width, Height - 1); x += c.Width + (int)(2 * K); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            using (var line = new SolidBrush(Color.FromArgb(20, 255, 255, 255))) g.FillRectangle(line, 0, Height - 1, Width, 1);
        }
    }

    sealed class TabButton : RoundButton
    {
        readonly string glyph;
        readonly bool selected;
        bool hover;

        public TabButton(string title, string glyph, bool selected)
        {
            Text = title;
            this.glyph = glyph;
            this.selected = selected;
            Font = selected ? Fonts.Semi(10f) : Fonts.Get(10f);
            FitText(21);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float k = K;
            Color fore = selected ? Color.White : hover ? Color.FromArgb(220, 220, 225) : Color.FromArgb(150, 150, 156);
            using (var icons = new Font("Segoe MDL2 Assets", 9.5f))
                TextRenderer.DrawText(g, glyph, icons, new Rectangle((int)(10 * k), 0, (int)(18 * k), Height - (int)(4 * k)), fore,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(32 * k), 0, Width - (int)(32 * k), Height - (int)(4 * k)), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (selected) Fill(g, new RectangleF(4 * k, Height - 3 * k, Width - 8 * k, 2.5f * k), 1.25f * k, Theme.Blue);
        }
    }
}

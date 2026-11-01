using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Emd.Core.Model;
using Emd.Core.Projects;
using Emd.Core.Validation;

namespace Emd.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void LoadProject(object? sender, RoutedEventArgs args)
    {
        var path = ProjectPath.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            return;

        var loaded = ProjectLoader.Default.LoadFile(path);
        PartsList.ItemsSource = null;
        DiagnosticsList.ItemsSource = null;
        SystemCanvas.Children.Clear();
        if (loaded.Project is null)
        {
            Status.Text = string.Join(Environment.NewLine, loaded.Diagnostics);
            return;
        }

        var diagnostics = ProjectValidator.Default.Validate(loaded.Project);
        var errors = diagnostics.Count(diagnostic =>
            diagnostic.Severity == Emd.Core.Diagnostics.DiagnosticSeverity.Error);
        Status.Text =
            $"{loaded.Project.Name} — {loaded.Project.Parts.Count} parts, " +
            $"{loaded.Project.Connections.Count} connections, {errors} error(s)";
        PathStatus.Text = System.IO.Path.GetFullPath(path);
        PartsList.ItemsSource = loaded.Project.Parts
            .OrderBy(part => part.Name)
            .Select(part => $"{part.Key ?? part.Name}  ({part.Category?.ToString() ?? part.Kind.ToString()})")
            .ToList();
        DiagnosticsList.ItemsSource = diagnostics
            .Select(diagnostic => diagnostic.ToString())
            .ToList();
        DrawSystem(loaded.Project);
    }

    private void DrawSystem(EmdProject project)
    {
        const double margin = 24;
        var bounds = project.Parts
            .Select(part => new
            {
                Part = part,
                X = part.Pose?.Pos?[0] ?? 0,
                Y = part.Pose?.Pos?[1] ?? 0,
                W = part.Geometry?.Size?[0] ?? 0.02,
                H = part.Geometry?.Size?[1] ?? 0.02
            })
            .ToList();
        if (bounds.Count == 0)
            return;

        var minX = bounds.Min(item => item.X);
        var minY = bounds.Min(item => item.Y);
        var maxX = bounds.Max(item => item.X + item.W);
        var maxY = bounds.Max(item => item.Y + item.H);
        var scale = Math.Min(
            (SystemCanvas.Bounds.Width - 2 * margin) / Math.Max(0.001, maxX - minX),
            (SystemCanvas.Bounds.Height - 2 * margin) / Math.Max(0.001, maxY - minY));
        if (!double.IsFinite(scale) || scale <= 0)
            scale = 1;

        foreach (var item in bounds)
        {
            var rectangle = new Rectangle
            {
                Width = Math.Max(4, item.W * scale),
                Height = Math.Max(4, item.H * scale),
                Fill = new SolidColorBrush(Color.FromArgb(90, 52, 152, 219)),
                Stroke = Brushes.SteelBlue,
                StrokeThickness = 1
            };
            Canvas.SetLeft(rectangle, margin + (item.X - minX) * scale);
            Canvas.SetTop(rectangle, margin + (item.Y - minY) * scale);
            SystemCanvas.Children.Add(rectangle);

            var label = new TextBlock
            {
                Text = item.Part.Key ?? item.Part.Name,
                FontSize = 10
            };
            Canvas.SetLeft(label, margin + (item.X - minX) * scale);
            Canvas.SetTop(label, margin + (item.Y - minY) * scale + item.H * scale + 2);
            SystemCanvas.Children.Add(label);
        }
    }
}

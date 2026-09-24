import System
import System.Linq
import System.Reflection
import ScottPlot
import ScottPlot.Avalonia

# Find PlotView type
var avaloniaAsm = typeof(ScottPlot.Avalonia.AvaPlot).Assembly
var plotViewTypes = avaloniaAsm.GetTypes().Where(t => t.Name.Contains("Plot")).ToList()
Console.WriteLine("=== ScottPlot.Avalonia types with 'Plot' in name ===")
foreach (var t in plotViewTypes)
    Console.WriteLine(t.FullName)

# Find Plot type in ScottPlot
var scottAsm = typeof(Plot).Assembly
var plotType = typeof(Plot)
Console.WriteLine("\n=== Plot type members (public instance) ===")
foreach (var m in plotType.GetMembers(BindingFlags.Public | BindingFlags.Instance)
    .Where(m => m.Name.Contains("Heat") || m.Name.Contains("Color") || m.Name.Contains("Axis") || m.Name.Contains("Label") || m.Name.Contains("Add") || m.Name.Contains("Remove")))
    Console.WriteLine(m.MemberType + " " + m.Name)

Console.WriteLine("\n=== Plot Add methods ===")
var addMethod = plotType.GetMethod("Add")
if (addMethod != null)
{
    foreach (var p in addMethod.GetParameters())
        Console.WriteLine(p.ParameterType.Name + " " + p.Name)
    // Also check extension methods
    var addImpl = addMethod.GetBaseDefinition()
    Console.WriteLine("Add return type: " + addMethod.ReturnType.Name)
}

# Check if Add is an extension method on Plot
var extMethods = scottAsm.GetTypes()
    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
    .Where(m => m.Name == "Add" && m.GetParameters().Length > 0)
    .ToList()
Console.WriteLine("\n=== Extension Add methods ===")
foreach (var m in extMethods)
{
    Console.WriteLine(m.DeclaringType?.FullName + "." + m.Name)
    foreach (var p in m.GetParameters())
        Console.WriteLine("  " + p.ParameterType.Name + " " + p.Name)
    Console.WriteLine("  Return: " + m.ReturnType.Name)
}

# Check IColormap interface
Console.WriteLine("\n=== IColormap interface ===")
var colormapTypes = scottAsm.GetTypes().Where(t => t.Name.Contains("Colormap")).ToList()
foreach (var t in colormapTypes)
{
    Console.WriteLine(t.FullName)
    foreach (var iface in t.GetInterfaces())
        Console.WriteLine("  Interface: " + iface.FullName)
}

# Find IColormap specifically
var icolormap = scottAsm.GetTypes().FirstOrDefault(t => t.Name == "IColormap")
if (icolormap != null)
{
    Console.WriteLine("\n=== IColormap members ===")
    foreach (var m in icolormap.GetMembers())
        Console.WriteLine(m.MemberType + " " + m.Name)
}

# Check Heatmap class
Console.WriteLine("\n=== Heatmap class ===")
var heatmapType = scottAsm.GetTypes().FirstOrDefault(t => t.Name == "Heatmap")
if (heatmapType != null)
{
    Console.WriteLine("Full name: " + heatmapType.FullName)
    Console.WriteLine("Base: " + heatmapType.BaseType?.FullName)
    foreach (var iface in heatmapType.GetInterfaces())
        Console.WriteLine("Interface: " + iface.FullName)
    foreach (var m in heatmapType.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        Console.WriteLine(m.MemberType + " " + m.Name + (m is MethodInfo mi ? "(" + string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")" : ""))
}

# Check Colormaps static class
Console.WriteLine("\n=== Colormaps static members ===")
var colormapsType = scottAsm.GetTypes().FirstOrDefault(t => t.Name == "Colormaps")
if (colormapsType != null)
{
    foreach (var m in colormapsType.GetMembers(BindingFlags.Public | BindingFlags.Static))
        Console.WriteLine(m.MemberType + " " + m.Name + (m is MethodInfo mi ? "(" + string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name)) + ")" : ""))
}

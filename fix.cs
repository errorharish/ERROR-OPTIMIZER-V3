using System;
using System.IO;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        string[] files = Directory.GetFiles(@"src\BiosOptimizer.Core", "*.cs", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            string content = File.ReadAllText(file);
            if (content.Contains("CheckAvailability("))
            {
                // Find CheckAvailability block
                var match = Regex.Match(content, @"TargetState CheckAvailability[^\{]+\{([^\}]+)\}");
                if (match.Success)
                {
                    string body = match.Groups[1].Value;
                    string newBody = body.Replace("return true;", "return TargetState.Ready;")
                                         .Replace("return false;", "return TargetState.NotApplicable;");
                    
                    if (body.Contains("_serviceManager.GetService") || body.Contains("_taskManager.FindTask") || body.Contains("package == null"))
                    {
                        newBody = newBody.Replace("return TargetState.NotApplicable;", "return TargetState.NotAvailable;");
                    }
                    if (body.Contains("IsProtectedTarget"))
                    {
                        newBody = newBody.Replace("return TargetState.NotAvailable;", "return TargetState.Protected;").Replace("return TargetState.NotApplicable;", "return TargetState.Protected;");
                    }

                    string newContent = content.Substring(0, match.Groups[1].Index) + newBody + content.Substring(match.Groups[1].Index + match.Groups[1].Length);
                    
                    // Specific fix for SetServiceStartupHandler which does eturn service != null;
                    newContent = Regex.Replace(newContent, @"return\s+[a-zA-Z0-9_]+\s*!=\s*null\s*;", "return TargetState.Ready; // Handled in Apply");
                    
                    File.WriteAllText(file, newContent);
                }
            }
        }
    }
}

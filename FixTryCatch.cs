using System;
using System.IO;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        string path = @"D:\PROJECTS FOR EXE\ON PROCESS\ERROR OPTIMIZER\src\BiosOptimizer.Service\NamedPipeServer.cs";
        string content = File.ReadAllText(path);
        
        // This is a naive heuristic but it works for standard C# files:
        // We look for 'try\s*{' and count braces. When we hit the matching '}', 
        // we check if it is followed by 'catch'. If not, we insert 'catch (Exception ex) { return Fail(ex.Message); }'
        
        var chars = content.ToCharArray();
        var newContent = new System.Text.StringBuilder();
        
        int i = 0;
        while (i < chars.Length)
        {
            if (i < chars.Length - 3 && chars[i] == 't' && chars[i+1] == 'r' && chars[i+2] == 'y')
            {
                // find the '{'
                int braceIndex = -1;
                for (int j = i + 3; j < i + 20 && j < chars.Length; j++) {
                    if (chars[j] == '{') { braceIndex = j; break; }
                }
                
                if (braceIndex != -1)
                {
                    newContent.Append(content.Substring(i, braceIndex - i + 1));
                    i = braceIndex + 1;
                    
                    int depth = 1;
                    while (depth > 0 && i < chars.Length)
                    {
                        if (chars[i] == '{') depth++;
                        if (chars[i] == '}') depth--;
                        newContent.Append(chars[i]);
                        i++;
                    }
                    
                    // Now check if 'catch' follows
                    int peek = i;
                    while (peek < chars.Length && char.IsWhiteSpace(chars[peek])) peek++;
                    
                    bool hasCatch = false;
                    if (peek < chars.Length - 5 && chars[peek] == 'c' && chars[peek+1] == 'a' && chars[peek+2] == 't' && chars[peek+3] == 'c' && chars[peek+4] == 'h')
                    {
                        hasCatch = true;
                    }
                    
                    if (!hasCatch)
                    {
                        newContent.Append("\r\n            catch (Exception ex) { return Fail(ex.Message); }");
                    }
                    continue;
                }
            }
            newContent.Append(chars[i]);
            i++;
        }
        
        File.WriteAllText(path, newContent.ToString());
    }
}

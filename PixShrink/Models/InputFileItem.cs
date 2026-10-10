using System;
using System.IO;

namespace PixShrink.Models;

/// <summary>One row of the input file list.</summary>
public record InputFileItem(string FullPath)
{
    public string Name
    {
        get
        {
            try
            {
                return Path.GetFileName(FullPath);
            }
            catch (ArgumentException ae)
            {
                throw new ArgumentException(ae.Message);
            }
        }
    }
}

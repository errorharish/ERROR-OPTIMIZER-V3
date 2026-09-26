namespace BiosOptimizer.Core.Implementations.Storage;

public class DriveInfoResult
{
    public string Name { get; set; } = string.Empty;
    public string DriveType { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public string DriveFormat { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long TotalFreeSpace { get; set; }
    public bool IsReady { get; set; }
}

public class StorageEngine
{
    public List<DriveInfoResult> GetDrives()
    {
        var result = new List<DriveInfoResult>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (d.IsReady)
            {
                result.Add(new DriveInfoResult
                {
                    Name = d.Name,
                    DriveType = d.DriveType.ToString(),
                    VolumeLabel = d.VolumeLabel,
                    DriveFormat = d.DriveFormat,
                    TotalSize = d.TotalSize,
                    TotalFreeSpace = d.TotalFreeSpace,
                    IsReady = d.IsReady
                });
            }
        }
        return result;
    }
}

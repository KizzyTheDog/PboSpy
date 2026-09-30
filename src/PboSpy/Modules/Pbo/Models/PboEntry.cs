using BIS.PBO;
using PboSpy.Interfaces;
using PboSpy.Models;
using PboSpy.Modules.Deobfuscate.Core;
using System.ComponentModel;
using System.IO;

namespace PboSpy.Modules.Pbo.Models;
public class PboEntry : FileBase, ITreeItem, INotifyPropertyChanged
{
    private readonly PBO pbo;
    private string renamedPath;

    public PboEntry(PBO pbo, IPBOFileEntry entry, ITreeItem parent)
    {
        this.pbo = pbo;
        OriginalName = NameRecovery.FixEncoding(System.IO.Path.GetFileName(entry.FileName));
        Extension = System.IO.Path.GetExtension(entry.FileName).ToLowerInvariant();
        Entry = entry;
        Parent = parent;
    }

    public PBO PBO => pbo;

    public IPBOFileEntry Entry { get; set; }

    public string OriginalName { get; }

    public override string Name => renamedPath != null ? System.IO.Path.GetFileName(renamedPath) : OriginalName;

    /// <summary>
    /// Path inside the PBO to use instead of the stored one. The PBO itself is never rewritten;
    /// export, extract and drag out use this name.
    /// </summary>
    public string RenamedPath
    {
        get => renamedPath;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? null : value.Replace('/', '\\').Trim('\\');
            if (value != null && string.Equals(value, StoredPath, StringComparison.Ordinal))
            {
                value = null;
            }
            if (value == renamedPath)
            {
                return;
            }
            renamedPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RenamedPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRenamed)));
        }
    }

    public bool IsRenamed => renamedPath != null;

    /// <summary>Like <see cref="FullPath"/> but with the rename applied.</summary>
    public string ExportFullPath => pbo.Prefix + "\\" + (renamedPath ?? StoredPath);

    /// <summary>Stored path with the name bytes read as UTF-8 where that is what they are.</summary>
    public string StoredPath => NameRecovery.FixEncoding(Entry.FileName).Replace('/', '\\').Trim('\\');

    public event PropertyChangedEventHandler PropertyChanged;

    public string Path => FullPath;

    public override string Extension { get; }

    public ICollection<ITreeItem> Children => null;

    public override string FullPath => pbo.Prefix + "\\" + Entry.FileName;

    public override int DataSize => Entry.Size;

    public ITreeItem Parent { get; set; }

    public override Stream GetStream()
    {
        return Entry.OpenRead();
    }

    internal void Extract(string fileName)
    {
        using var stream = File.Create(fileName);
        using var source = Entry.OpenRead();
        source.CopyTo(stream);
    }

    public override bool Equals(object obj)
    {
        return obj is PboEntry entry &&
               FullPath == entry.FullPath;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(FullPath);
    }
}

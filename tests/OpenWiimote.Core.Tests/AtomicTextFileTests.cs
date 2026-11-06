using OpenWiimote.Core.Storage;
using Xunit;
namespace OpenWiimote.Core.Tests;
public sealed class AtomicTextFileTests
{
    [Fact]
    public async Task ConcurrentReadersAndWritersNeverSeePartialContentsOrBlockWindowsReplacement()
    {
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var path=Path.Combine(root,"data.json");var values=new[]{new string('a',4096),new string('b',8192)};
        try
        {
            AtomicTextFile.Write(path,values[0]);
            using var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            await Task.WhenAll(Enumerable.Range(0,16).Select(n=>Task.Run(()=>
            {
                for(var k=0;k<10;k++){AtomicTextFile.Write(path,values[n%2]);Assert.Contains(AtomicTextFile.Read(path),values);}
            })));
            Assert.Contains(AtomicTextFile.Read(path),values);Assert.Empty(Directory.GetFiles(root,"*.tmp"));
            Assert.Throws<IOException>(()=>AtomicTextFile.Write(path,"must not overwrite",overwrite:false));
            Assert.Contains(AtomicTextFile.Read(path),values);
        }
        finally{Directory.Delete(root,true);}
    }
}

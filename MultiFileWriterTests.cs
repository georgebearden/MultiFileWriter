using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace GFileWriter
{
  public class MultiFileWriterTests
  {
    public class TempFile : IDisposable
    {
      public TempFile()
      {
        Name = Path.GetTempFileName();
      }

      public string Name { get; private set; }

      public void Dispose()
      {
        File.Delete(Name);
      }
    }

    [Fact]
    public void WriteThreadIsDeadIfNoWritersAreCreated()
    {
      using (var multiFileWriter = new MultiFileWriter())
      {
        var isWriteThreadAlive = multiFileWriter.IsWriteThreadAlive;
        Assert.False(isWriteThreadAlive);
      }
    }

    [Fact]
    public void WriteThreadIsAliveIfSingleWriterIsCreated()
    {
      using (var filePath = new TempFile())
      using (var multiFileWriter = new MultiFileWriter())
      {
        var lineWriter = multiFileWriter.Create(filePath.Name);

        var isWriteThreadAlive = multiFileWriter.IsWriteThreadAlive;
        Assert.True(isWriteThreadAlive);
      }
    }

    [Fact]
    public void NotDisposingWritersStillCleansUpOnDispose()
    {
      var filePath1 = Path.GetTempFileName();
      var filePath2 = Path.GetTempFileName();
      var multiFileWriter = new MultiFileWriter();
      var lineWriter1 = multiFileWriter.Create(filePath1);
      var lineWriter2 = multiFileWriter.Create(filePath2);

      Assert.Equal(expected: 2, actual: multiFileWriter.FilePaths.Count());

      // Dispose of the multiFileWriter instead of the individual writers to make
      // sure bad clients that dont clean up after themselves are still cleaned up
      multiFileWriter.Dispose();
      Assert.Empty(multiFileWriter.FilePaths);

      File.Delete(filePath1);
      File.Delete(filePath2);
    }

    [Fact]
    public void DisposingOfSingleWriterStopsWriteThread()
    {
      using (var filePath = new TempFile())
      using (var multiFileWriter = new MultiFileWriter())
      {
        Assert.False(multiFileWriter.IsWriteThreadAlive);
        using (var writer = multiFileWriter.Create(filePath.Name))
        {
          Assert.True(multiFileWriter.IsWriteThreadAlive);
        }
        Assert.False(multiFileWriter.IsWriteThreadAlive);
      }
    }

    [Fact]
    public void DisposingOfWriterDoesNotStopWriteThreadIfOtherWritersExist()
    {
      using (var filePath1 = new TempFile())
      using (var filePath2 = new TempFile())
      using (var multiFileWriter = new MultiFileWriter())
      {
        var lineWriter1 = multiFileWriter.Create(filePath1.Name);
        Assert.True(multiFileWriter.IsWriteThreadAlive);
        var lineWriter2 = multiFileWriter.Create(filePath2.Name);
        Assert.True(multiFileWriter.IsWriteThreadAlive);
        lineWriter1.Dispose();
        Assert.True(multiFileWriter.IsWriteThreadAlive);
        lineWriter2.Dispose();
        Assert.False(multiFileWriter.IsWriteThreadAlive);
      }
    }

    [Fact]
    public void CallingWriteLineAfterDisposeThrowsObjectDisposedException()
    {
      using (var filePath = new TempFile())
      using (var multiFileWriter = new MultiFileWriter())
      {
        var lineWriter = multiFileWriter.Create(filePath.Name);
        lineWriter.Dispose();
        Assert.Throws<ObjectDisposedException>(() => lineWriter.WriteLine(string.Empty));
      }
    }

    [Fact]
    public void NullFilePathThrowsNullArgumentExceptionOnCreate()
    {
      using (var multiFileWriter = new MultiFileWriter())
      {
        Assert.Throws<ArgumentNullException>(() => multiFileWriter.Create(null));
      }
    }

    [Fact]
    public void DuplicateFilePathThrowsArgumentExceptionOnCreate()
    {
      using (var filePath = new TempFile())
      using (var multiFileWriter = new MultiFileWriter())
      {
        using (var lineWriter = multiFileWriter.Create(filePath.Name))
        {
          Assert.Throws<ArgumentException>(() => multiFileWriter.Create(filePath.Name));
        }
      }
    }

    [Fact]
    public void CanReadInFileCreatedByLineWriter()
    {
      var filePath = Path.GetTempFileName();
      var line1 = "line1";
      var line2 = "line2";
      var line3 = "line3";
      var line4 = "line4";
      var line5 = "line5";
      
      using (var multiFileWriter = new MultiFileWriter())
      {
        using (var lineWriter = multiFileWriter.Create(filePath))
        {
          lineWriter.WriteLine(line1);
          lineWriter.WriteLine(line2);
          lineWriter.WriteLine(line3);
          lineWriter.WriteLine(line4);
          lineWriter.WriteLine(line5);
        }
      }

      var inFile = File.ReadAllLines(filePath);
      Assert.Equal(expected: 5, actual: inFile.Count());
      File.Delete(filePath);
    }

    [Fact]
    public void CanWriteToMultipleFiles()
    {
      var filePath1 = Path.GetTempFileName();
      var filePath2 = Path.GetTempFileName();
      var line1 = "line1";
      var line2 = "line2";
      var line3 = "line3";
      var line4 = "line4";
      var line5 = "line5";

      using (var multiFileWriter = new MultiFileWriter())
      {
        using (var lineWriter1 = multiFileWriter.Create(filePath1))
        using (var lineWriter2 = multiFileWriter.Create(filePath2))
        {
          lineWriter1.WriteLine(line1);
          lineWriter2.WriteLine(line1);
          lineWriter1.WriteLine(line2);
          lineWriter2.WriteLine(line2);
          lineWriter1.WriteLine(line3);
          lineWriter2.WriteLine(line3);
          lineWriter1.WriteLine(line4);
          lineWriter2.WriteLine(line4);
          lineWriter1.WriteLine(line5);
          lineWriter2.WriteLine(line5);
        }
      }

      var inFile1 = File.ReadAllLines(filePath1);
      Assert.Equal(expected: 5, actual: inFile1.Count());
      File.Delete(filePath1);
      var inFile2 = File.ReadAllLines(filePath2);
      Assert.Equal(expected: 5, actual: inFile2.Count());
      File.Delete(filePath2);
    }

    [Fact]
    public void CanWriteAndReadJson()
    {
      var filePath = Path.GetTempFileName();
      JsonSerializerSettings settings = new JsonSerializerSettings
      {
        TypeNameHandling = TypeNameHandling.All
      };

      using (var multiFileWriter = new MultiFileWriter())
      {
        using (var lineWriter = multiFileWriter.Create(filePath))
        {
          var class1 = new DerivedClass1();
          var class1Json = JsonConvert.SerializeObject(class1, settings);
          lineWriter.WriteLine(class1Json);

          var class2 = new DerivedClass2();
          var class2Json = JsonConvert.SerializeObject(class2, settings);
          lineWriter.WriteLine(class2Json);
        }
      }

      var inFile = File.ReadAllLines(filePath);
      Assert.Equal(expected: 2, actual: inFile.Count());

      var outClass1 = JsonConvert.DeserializeObject<BaseClass>(inFile.First(), settings);
      Assert.IsType<DerivedClass1>(outClass1);

      var outClass2 = JsonConvert.DeserializeObject<BaseClass>(inFile.Last(), settings);
      Assert.IsType<DerivedClass2>(outClass2);

      File.Delete(filePath);
    }

    [Fact]
    public void LineWriterCanClearLargePendingListOnDispose()
    {
      using (var filePath = new TempFile())
      using (var multiFileWriter = new MultiFileWriter())
      using (var lineWriter = multiFileWriter.Create(filePath.Name))
      {
        bool running = true;
        // Enqueue way too many writes that file io cant keep up with.
        new Thread(() =>
        {
          while (running)
          {
            lineWriter.WriteLine("line");
          }
        }).Start();
        Thread.Sleep(500);
        running = false;
      }
    }

    [Fact]
    public void MultiFileWriterCanClearManyPendingListsOnDispose()
    {
      using (var filePath1 = new TempFile())
      using (var filePath2 = new TempFile())
      using (var multiFileWriter = new MultiFileWriter())
      {
        // force the multifilewriter to dispose of these manually by leaving
        // them out of their own using blocks.
        var lineWriter1 = multiFileWriter.Create(filePath1.Name);
        var lineWriter2 = multiFileWriter.Create(filePath2.Name);

        bool running = true;
        // Enqueue way too many writes that file io cant keep up with.
        new Thread(() =>
        {
          while (running)
            lineWriter1.WriteLine("line");
        }).Start();
        new Thread(() =>
        {
          while (running)
            lineWriter2.WriteLine("line");
        }).Start();
        Thread.Sleep(500);
        running = false;
      }
    }
  }
}

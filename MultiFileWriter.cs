using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace GFileWriter
{
  public interface ILineWriter : IDisposable
  {
    void WriteLine(string line);
  }

  public class MultiFileWriter : IDisposable
  {
    private static BlockingCollection<WriteAction> _lines = new BlockingCollection<WriteAction>();
    private static int _instanceCount = 0;
    private readonly Dictionary<string, LineWriter> _writers = new Dictionary<string, LineWriter>();
    private Thread _writeThread;

    public MultiFileWriter()
    {
      _instanceCount++;
      if (_instanceCount > 1)
        Debug.WriteLine("There should really only be once instance of MultiFileWriter per application, suggest to refactor your usage of it to do so.");
    }

    public ILineWriter Create(string filePath)
    {
      if (filePath == null)
        throw new ArgumentNullException("filePath is null");
      if (_writers.ContainsKey(filePath))
        throw new ArgumentException("filePath is already being used by another LineWriter");

      LineWriter lineWriter = new LineWriter(filePath, 
        disposeCallback: () =>
        {
          if (_writers.ContainsKey(filePath))
            _writers.Remove(filePath);

          // If there are no more writers then stop the write thread.
          if (_writers.Count == 0)
            StopWriteThread();
        });

      // Start the write thread if this is the first writer we are creating.
      if (_writers.Count == 0)
        StartWriteThread();

      _writers.Add(lineWriter.FilePath, lineWriter);
      return lineWriter;
    }

    public void Dispose()
    {
      // Clean up any dangling writers that were not cleaned up elsewhere.
      // Iterate the collection in reverse order to avoid the collection modified
      // while enumerating exception.
      for (int i = _writers.Count - 1; i >= 0; i--)
      {
        var key = _writers.Keys.ElementAt(i);
        var value = _writers[key];
        value.Dispose();
        _writers.Remove(key);
      }
    }

    private void StartWriteThread()
    {
      if (_writeThread != null && _writeThread.IsAlive)
        throw new InvalidOperationException("writeThread is already alive");

      _lines = new BlockingCollection<WriteAction>();
      _writeThread = new Thread(() =>
      {
        while (!_lines.IsCompleted)
        {
          WriteAction writeAction;
          if (_lines.TryTake(out writeAction, TimeSpan.FromSeconds(1)))
          {
            writeAction.Write();
          }
        }
      })
      { Name = "MultiFileWriter", IsBackground = true };
      _writeThread.Start();
    }

    private void StopWriteThread()
    {
      if (_writeThread == null || !_writeThread.IsAlive)
        throw new InvalidOperationException("writeThread is already stopped");

      _lines.CompleteAdding();
      _writeThread.Join();
      _writeThread = null;
    }

    public bool IsWriteThreadAlive
    {
      get
      {
        return _writeThread == null ? false : _writeThread.IsAlive;
      }
    }

    public IEnumerable<string> FilePaths { get { return _writers.Keys; } }

    private class WriteAction
    {
      public WriteAction(string filePath, Action write)
      {
        FilePath = filePath;
        Write = write;
      }

      public string FilePath { get; private set; }
      public Action Write { get; private set; }
    }

    private class LineWriter : ILineWriter
    {
      private readonly FileStream _fileStream;
      private readonly StreamWriter _streamWriter;
      private readonly Action _disposeCallback;

      public LineWriter(string filePath, Action disposeCallback)
      {
        FilePath = filePath;
        if (File.Exists(FilePath))
          File.Delete(FilePath);
        _fileStream = File.Open(FilePath, FileMode.CreateNew);
        _streamWriter = new StreamWriter(_fileStream) { AutoFlush = true };
        _disposeCallback = disposeCallback;
      }

      public void Dispose()
      {
        int linesLeft = int.MaxValue;
        do
        {
          linesLeft = _lines.Count(l => l.FilePath == FilePath);
          // TODO swap Debug.WriteLine for our logging classes.
          Debug.WriteLine("LineWriter({0}) has {1} pending writes before it can be disposed", FilePath, linesLeft);
          Thread.Sleep(50);
        } while (linesLeft > 0);

        _streamWriter.Dispose();
        _fileStream.Dispose();
        _disposeCallback();
        IsDisposed = true;
      }

      public void WriteLine(string line)
      {
        if (IsDisposed)
          throw new ObjectDisposedException(string.Format("LineWriter{{0}}", FilePath));

        if (!_lines.IsAddingCompleted)
          _lines.Add(new WriteAction(FilePath, () => _streamWriter.WriteLine(line)));
      }

      public string FilePath { get; private set; }
      public bool IsDisposed { get; private set; }
    }
  }
}

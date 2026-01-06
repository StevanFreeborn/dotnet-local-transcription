#:property BuiltInComInteropSupport=true

#:package FFMpegCore@5.2.0
#:package NAudio@2.2.1
#:package Whisper.net.AllRuntimes@1.8.1

using FFMpegCore;
using FFMpegCore.Enums;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

if (args.Length == 0)
{
  Console.WriteLine("Please provide the path to the video file.");
  return;
}

var videoPath = args[0];

if (File.Exists(videoPath) is false)
{
  Console.WriteLine("File does not exist: " + videoPath);
  return;
}

var tempWavPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

try
{
  await FFMpegArguments
      .FromFileInput(videoPath)
      .OutputToFile(tempWavPath, true, options => options
          .DisableChannel(Channel.Video)
          .ForceFormat("wav")
          .WithAudioSamplingRate(16000)
      )
      .ProcessAsynchronously();

  using var waveReader = new WaveFileReader(tempWavPath);

  var segmentDuration = TimeSpan.FromMinutes(2);
  var totalDuration = waveReader.TotalTime;
  var numOfSegments = (int)Math.Ceiling(totalDuration.TotalSeconds / segmentDuration.TotalSeconds);

  using var processor = await GetProcessor();

  foreach (var i in Enumerable.Range(0, numOfSegments))
  {
    using var segmentReader = new WaveFileReader(tempWavPath);

    var segment = segmentReader.ToSampleProvider()
        .Skip(i * segmentDuration)
        .Take(segmentDuration);

    var segmentProvider = segment.ToWaveProvider16();
    using var segmentStream = new MemoryStream();
    WaveFileWriter.WriteWavFileToStream(segmentStream, segmentProvider);

    segmentStream.Position = 0;

    var durationOffset = TimeSpan.FromSeconds(i * segmentDuration.TotalSeconds);

    await foreach (var result in processor.ProcessAsync(segmentStream))
    {
      var startTime = result.Start + durationOffset;
      var endTime = result.End + durationOffset;
      Console.WriteLine($"[{startTime:hh\\:mm\\:ss} - {endTime:hh\\:mm\\:ss}]: {result.Text}");
    }
  }
}
finally
{
  // Cleanup temporary file
  if (File.Exists(tempWavPath))
  {
    try
    {
      File.Delete(tempWavPath);
    }
    catch
    {
      // Best effort cleanup 
    }
  }
}

static async Task<WhisperProcessor> GetProcessor()
{
  using var memoryStream = new MemoryStream();
  var model = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.BaseEn);
  await model.CopyToAsync(memoryStream);
  var whisperFactory = WhisperFactory.FromBuffer(memoryStream.ToArray());
  return whisperFactory.CreateBuilder()
      .WithLanguage("en")
      .Build();
}
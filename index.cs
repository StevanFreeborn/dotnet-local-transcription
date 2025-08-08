#:property BuiltInComInteropSupport=true

#:package FFMpegCore@5.2.0
#:package NAudio@2.2.1
#:package Whisper.net.AllRuntimes@1.8.1

using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
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

using var mp4Stream = File.OpenRead(videoPath);
using var mp3Stream = new MemoryStream();

await FFMpegArguments
  .FromPipeInput(new StreamPipeSource(mp4Stream))
  .OutputToPipe(
    new StreamPipeSink(mp3Stream),
    static o => o.DisableChannel(Channel.Video).ForceFormat("mp3")
  )
  .ProcessAsynchronously();

mp3Stream.Position = 0;
using var reader = new Mp3FileReader(mp3Stream);
var outFormat = new WaveFormat(16000, reader.WaveFormat.Channels);
using var resampler = new MediaFoundationResampler(reader, outFormat);
using var waveStream = new MemoryStream();
WaveFileWriter.WriteWavFileToStream(waveStream, resampler);

waveStream.Position = 0;
using var waveReader = new WaveFileReader(waveStream);
var segmentDuration = TimeSpan.FromMinutes(2);
var totalDuration = waveReader.TotalTime;
var numOfSegments = (int)Math.Ceiling(totalDuration.TotalSeconds / segmentDuration.TotalSeconds);

foreach (var i in Enumerable.Range(0, numOfSegments))
{
  waveStream.Position = 0;
  using var segmentReader = new WaveFileReader(waveStream);
  var segment = segmentReader.ToSampleProvider()
    .Skip(i * segmentDuration)
    .Take(segmentDuration);

  var segmentProvider = segment.ToWaveProvider16();
  var segmentStream = new MemoryStream();
  WaveFileWriter.WriteWavFileToStream(segmentStream, segmentProvider);

  segmentStream.Position = 0;
  using var processor = await GetProcessor();
  var durationOffset = TimeSpan.FromSeconds(i * segmentDuration.TotalSeconds);

  await foreach (var result in processor.ProcessAsync(segmentStream))
  {
    var startTime = result.Start + durationOffset;
    var endTime = result.End + durationOffset;
    Console.WriteLine($"[{startTime:hh\\:mm\\:ss} - {endTime:hh\\:mm\\:ss}]: {result.Text}");
  }
}

















static async Task<WhisperProcessor> GetProcessor()
{
  using var memoryStream = new MemoryStream();
  var model = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.TinyEn);
  await model.CopyToAsync(memoryStream);
  var whisperFactory = WhisperFactory.FromBuffer(memoryStream.ToArray());
  return whisperFactory.CreateBuilder()
    .WithLanguage("en")
    .Build();
}
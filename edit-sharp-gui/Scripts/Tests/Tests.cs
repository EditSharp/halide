using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharp.Components.Nodes;
using EditSharp.Components.Nodes.Effects;
using EditSharp.Components.Media;
using EditSharp.Components.Nodes.Input;
using EditSharp.Rendering;
using EditSharp.Video;
using SkiaSharp;

public static class Tests
{
    // the media clips play 13 seconds of their file, as the old per-clip sources did
    static T Trim<T>(T clip, double seconds) where T : Clip
    {
        foreach (InputNode input in clip.Graph.InputNodes) input.Duration = TimeSpan.FromSeconds(seconds);
        return clip;
    }

    public static Blueprint TestBlueprint
    {
        get
        {
            VideoMedia videoMedia = new()
            {
                Path = Path.Combine(AppContext.BaseDirectory, "Media", "Example", "Video", "video3.mp4")
            };

            Timeline timeline = new();

            //add channels to timeline
            for (int i = 0; i < 8; i++)
            {
                VideoChannel channel = new()
                {
                    Name = $"Video {i}"
                };

                timeline.AddChannel(channel);
            }

            AudioChannel audioChannel = new()
                {
                    Name = $"Audio"
                };
            timeline.AddChannel(audioChannel);

            AudioChannel audioChannel2 = new()
                {
                    Name = $"Audio Jungle"
                };
            timeline.AddChannel(audioChannel2);

            //background
            VideoClip noise = VideoClip.CreateNoise(TimeSpan.Zero, TimeSpan.FromSeconds(13));
            noise.Name = $"Background Noise";
            timeline.Channels[0].AddClip(noise);

            //start blue
            noise.Graph.Nodes.OfType<TintNode>().Single().Color
                .GetOrCreateTrack()
                .AddKeyframe(TimeSpan.Zero, SKColors.White.WithRed(128).WithGreen(128));
            
            //turn green
            noise.Graph.Nodes.OfType<TintNode>().Single().Color
                .GetOrCreateTrack()
                .AddKeyframe(TimeSpan.FromSeconds(1), SKColors.White.WithRed(128).WithBlue(128));
            
            //stay green until second 12
            noise.Graph.Nodes.OfType<TintNode>().Single().Color
                .GetOrCreateTrack()
                .AddKeyframe(TimeSpan.FromSeconds(12), SKColors.White.WithRed(128).WithBlue(128));

            //turn red
            noise.Graph.Nodes.OfType<TintNode>().Single().Color
                .GetOrCreateTrack()
                .AddKeyframe(TimeSpan.FromSeconds(13), SKColors.White.WithGreen(128).WithBlue(128));

            //videos
            List<VideoClip> videos = [];
            for (int i = 1; i < 7; i++)
            {
                VideoClip video = Trim(VideoClip.CreateFromMedia(videoMedia, TimeSpan.Zero, TimeSpan.FromSeconds(13)), 13);

                video.Name = $"Video {i}";

                //video.Start += TimeSpan.FromSeconds(0.1f * i);

                RoundedCornersNode rounded = new();
                video.Graph.AddNode(rounded);

                DropShadowNode dropShadow = new();
                video.Graph.AddNode(dropShadow);

                InputNode inputNode = video.Graph.InputNodes.Single();
                TintNode tintNode = video.Graph.Nodes.OfType<TintNode>().Single();
                TransformNode transform = video.Graph.Nodes.OfType<TransformNode>().Single();

                //disconnect input node from tint node
                video.Graph.Disconnect(video.Graph.Connections.First(c => c.FromNodeId == inputNode.Id));

                //connect input node to rounded corners node
                video.Graph.Connect(
                    inputNode.Id, inputNode.Ports.Single().Name, 
                    rounded.Id, rounded.Ports.First(p => p.Direction == PortDirection.Input).Name);

                //connect rounded corners node to tint node
                video.Graph.Connect(
                    rounded.Id, rounded.Ports.First(p => p.Direction == PortDirection.Output).Name, 
                    tintNode.Id, tintNode.Ports.First(p => p.Direction == PortDirection.Input).Name);

                //disconnect transform from output
                video.Graph.Disconnect(video.Graph.Connections.First(c => c.FromNodeId == transform.Id));

                //connect transform to drop shadow
                video.Graph.Connect(
                    transform.Id, transform.Ports.First(p => p.Direction == PortDirection.Output).Name,
                    dropShadow.Id, dropShadow.Ports.First(p => p.Direction == PortDirection.Input).Name);

                //connect drop shadow to output
                video.Graph.Connect(
                    dropShadow.Id, dropShadow.Ports.First(p => p.Direction == PortDirection.Output).Name,
                    video.Graph.OutputNode.Id, video.Graph.OutputNode.Ports.First().Name);

                timeline.Channels[i].AddClip(video);

                videos.Add(video);
            }

            //timeline.Link(videos);

            // clip further out so i can test my gui
            VideoClip extraVideo = Trim(VideoClip.CreateFromMedia(videoMedia, TimeSpan.FromSeconds(13.75), TimeSpan.FromSeconds(7)), 13);
            timeline.Channels[6].AddClip(extraVideo);

            // clip further out so i can test my gui
            VideoClip extraVideo2 = Trim(VideoClip.CreateFromMedia(videoMedia, TimeSpan.FromSeconds(13.75), TimeSpan.FromSeconds(7)), 13);
            timeline.Channels[7].AddClip(extraVideo2);
            
            //sound
            AudioClip audio = Trim(AudioClip.CreateFromMedia(videoMedia.Audio, TimeSpan.Zero, TimeSpan.FromSeconds(13)), 13);
            audio.Name = $"Source Audio";
            timeline.AudioChannels[0].AddClip(audio);

            Blueprint blueprint = new()
            {
                RenderSettings = new()
                {
                    Resolution = new(1920, 1080),
                    Framerate = 30,
                    VideoCodec = VideoCodec.H265,
                    AudioCodec = AudioCodec.AAC,
                    //HardwareAccelerator = HardwareAccelerator.None
                },
                Timeline = timeline,
                OutputPath = Path.Combine(AppContext.BaseDirectory, "skibidi.mp4")
            };

            
            TestAnimator.AnimateCollage([
                blueprint.Timeline.Channels[1].Clips.First(), 
                blueprint.Timeline.Channels[2].Clips.First(),
                blueprint.Timeline.Channels[3].Clips.First(),
                blueprint.Timeline.Channels[4].Clips.First(),
                blueprint.Timeline.Channels[5].Clips.First(),
                blueprint.Timeline.Channels[6].Clips.First(),

            ]);
            

            return blueprint;
        }
    }
}

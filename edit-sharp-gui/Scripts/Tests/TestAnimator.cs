using EditSharp.Components;
using EditSharp.Components.Clips;
using EditSharp.Components.Nodes.Effects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

public static class TestAnimator
{
    public static void AnimateClip(Clip clip, Direction? direction = null, Vector2? position = null, Vector2? scale = null, float rotation = 0f)
    {
        //pick random direction for the video to come in from if none is specified
        direction ??= (Direction)Random.Shared.Next(0, 4);

        position ??= Vector2.Zero;
        scale ??= new(0.9f, 0.9f);

        ClipTransform transform = clip.Graph.Nodes.OfType<TransformNode>().Single().Transform;

        //start position
        SetKeyframe(transform, TimeSpan.Zero, StartPositions[direction.Value], scale.Value, -rotation, 0f, 0f);

        //move into center frame
        SetKeyframe(transform, TimeSpan.FromSeconds(1), position.Value, scale.Value, rotation, RandomRange(-0.6f, 0.6f), RandomRange(-0.6f, 0.6f));

        //random pitch + yaw while on screen
        TimeSpan currentTime = TimeSpan.FromSeconds(3f);
        while (currentTime < clip.Duration - TimeSpan.FromSeconds(3f))
        {
            Vector2 newPosition = position.Value + new Vector2(RandomRange(-0.0025f, 0.0025f), RandomRange(-0.0025f, 0.0025f));
            SetKeyframe(transform, currentTime, newPosition, scale.Value, rotation, RandomRange(-0.6f, 0.6f), RandomRange(-0.6f, 0.6f));

            currentTime += TimeSpan.FromSeconds(2f);
        }

        //move out of center frame
        Vector2 newPosition1 = position.Value + new Vector2(RandomRange(-0.0025f, 0.0025f), RandomRange(-0.0025f, 0.0025f));
        SetKeyframe(transform, clip.Duration - TimeSpan.FromSeconds(1), newPosition1, scale.Value, rotation, RandomRange(-0.6f, 0.6f), RandomRange(-0.6f, 0.6f));

        //end position.
        SetKeyframe(transform, clip.Duration, EndPositions[direction.Value], scale.Value, 0f, 0f, 0f);
    }

    public static void AnimateComparison(List<Clip> clips /* list should contain 2 clips */)
    {
        if (clips.Count != 2) throw new ArgumentException("clip comparison requires exactly two clips");

        //pick random direction for the video to go (any direction but left)
        Direction direction = (Direction)Random.Shared.Next(1, 4);

        //animate first video
        AnimateClip(clips[0], direction, new(-0.5f, 0.025f), new(0.48f, 0.48f), -0.5f);

        //animate second video
        AnimateClip(clips[1], OppositeDirections[direction], new(0.5f, -0.025f), new(0.48f, 0.48f), 0.5f);
    }

    public static void AnimateCollage(List<Clip> clips /* list should contain 6 clips */)
    {
        if (clips.Count != 6) throw new ArgumentException("clip collage requires exactly six clips");

        //animate first video (top left)
        AnimateClip(clips[0], Direction.Right, new(-0.67f, 0.325f), new(0.32f, 0.31f), -0.5f);

        //animate second video (top center)
        AnimateClip(clips[1], Direction.Down, new(0f, 0.35f), new(0.32f, 0.31f));

        //animate third video (top right)
        AnimateClip(clips[2], Direction.Left, new(0.67f, 0.325f), new(0.32f, 0.31f), 0.5f);

        //animate fourth video (bottom left)
        AnimateClip(clips[3], Direction.Right, new(-0.67f, -0.325f), new(0.32f, 0.31f), 0.5f);

        //animate fifth video (bottom center)
        AnimateClip(clips[4], Direction.Up, new(0f, -0.35f), new(0.32f, 0.31f));

        //animate sixth video (bottom right)
        AnimateClip(clips[5], Direction.Left, new(0.67f, -0.325f), new(0.32f, 0.31f), -0.5f);
    }

    static void SetKeyframe(ClipTransform transform, TimeSpan time, Vector2? position, Vector2? scale, float rotation = 0f, float pitch = 0f, float yaw = 0f)
    {
        position ??= Vector2.Zero;
        scale ??= Vector2.One;

        //set position
        var keyframe = transform.UsePositionTrack().AddKeyframe(time, position.Value);
        keyframe.InInterpolation = InterpolationType.Bezier;
        keyframe.OutInterpolation = InterpolationType.Bezier;

        //set scale
        var keyframe1 = transform.Scale.GetOrCreateTrack().AddKeyframe(time, scale.Value);
        keyframe1.InInterpolation = InterpolationType.Bezier;
        keyframe1.OutInterpolation = InterpolationType.Bezier;

        //set rotation
        var keyframe2 = transform.Rotation.GetOrCreateTrack().AddKeyframe(time, rotation);
        keyframe2.InInterpolation = InterpolationType.Bezier;
        keyframe2.OutInterpolation = InterpolationType.Bezier;

        //set pitch
        var keyframe3 = transform.Pitch.GetOrCreateTrack().AddKeyframe(time, pitch);
        keyframe3.InInterpolation = InterpolationType.Bezier;
        keyframe3.OutInterpolation = InterpolationType.Bezier;
        
        //set yaw
        var keyframe4 = transform.Yaw.GetOrCreateTrack().AddKeyframe(time, yaw);
        keyframe4.InInterpolation = InterpolationType.Bezier;
        keyframe4.OutInterpolation = InterpolationType.Bezier;
    }

    public enum Direction
    {
        Left,
        Right,
        Up,
        Down
    }

    static readonly Dictionary<Direction, Direction> OppositeDirections = new()
    {
        { Direction.Left, Direction.Right },
        { Direction.Right, Direction.Left },
        { Direction.Up, Direction.Down },
        { Direction.Down, Direction.Up }
    };

    static readonly Dictionary<Direction, Vector2> StartPositions = new()
    {
        { Direction.Left, new(2, 0) },
        { Direction.Right, new(-2, 0) },
        { Direction.Up, new(0, -2) },
        { Direction.Down, new(0, 2) }
    };

    static readonly Dictionary<Direction, Vector2> EndPositions = new()
    {
        { Direction.Left, new(-2, 0) },
        { Direction.Right, new(2, 0) },
        { Direction.Up, new(0, 2) },
        { Direction.Down, new(0, -2) }
    };

    //returns float value between min and max (both inclusive)
    public static float RandomRange(float min, float max)
    {
        return float.Lerp(min, max, Random.Shared.NextSingle());
    }
}

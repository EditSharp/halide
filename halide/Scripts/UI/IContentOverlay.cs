namespace Halide.Scripts.UI;

// a control laid over a content control that is not part of the content: a
// selection box, a guide line. FitToChildren leaves these out of its measure,
// so an overlay dragged past the last clip does not make the timeline longer
public interface IContentOverlay { }

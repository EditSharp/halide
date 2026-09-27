// rasterizes audio straight from its frequency-domain frames. the texture
// holds, per frame, 32 band energies (eight RGBA texels) and the peak (a
// ninth), tiled in rows of row_width frames. the control's x maps to
// content time through content_at_left and pixel_seconds, so trims,
// stretches and zooms redraw without new data.
//
// every pixel is rasterized the same way, whatever the zoom: the wave is
// synthesised from the bands - each band a sine at its centre frequency
// with the energy the frames give it - at a run of moments across the
// pixel's time, and the pixel is filled between the lowest and highest
// values found. bands too high to resolve at the zoom widen the wave as
// noise instead. the rms level from the bands draws a brighter body
// inside. edges take the fraction of the pixel the fill covers, so they
// come out smooth
shader_type canvas_item;

uniform sampler2D spectrum : filter_nearest, repeat_disable;
uniform int frame_count = 0;
uniform int row_width = 4096;
uniform float frame_seconds = 0.010666667;
uniform float content_start = 0.0;
uniform float content_at_left = 0.0;
uniform float pixel_seconds = 0.01;
uniform vec4 peak_color : source_color = vec4(1.0);
uniform vec4 rms_color : source_color = vec4(1.0);
uniform vec2 size = vec2(100.0, 50.0);
uniform float radius = 2.0;

const int BANDS = 32;
const int TEXELS_PER_FRAME = 9;
const int SAMPLES = 24;
const float TWO_PI = 6.28318530718;

varying vec2 local;

void vertex() {
	local = VERTEX;
}

// band b's centre frequency: log spaced from 20 Hz to 20 kHz
float band_hz(int b) {
	return 20.0 * pow(1000.0, (float(b) + 0.5) / 32.0);
}

ivec2 texel_of(int frame, int k) {
	int row = frame / row_width;
	int col = frame - row * row_width;
	return ivec2(col, row * TEXELS_PER_FRAME + k);
}

// the bands of a frame, four to a texel
void bands_of(int frame, out vec4 e[8]) {
	for (int k = 0; k < 8; k++) e[k] = texelFetch(spectrum, texel_of(frame, k), 0);
}

float peak_of(int frame) {
	return texelFetch(spectrum, texel_of(frame, 8), 0).r;
}

float band(vec4 e[8], int b) {
	vec4 t = e[b / 4];
	int c = b - (b / 4) * 4;
	return c == 0 ? t.r : c == 1 ? t.g : c == 2 ? t.b : t.a;
}

// the wave at one content time from the bands the zoom resolves, the rms
// of every band, and the rms of the bands it cannot resolve
void synth(float t, float nyquist, out float value, out float rms, out float unresolved) {
	float f = (t - content_start) / frame_seconds;
	int frame = int(floor(f));
	float frac = f - floor(f);

	value = 0.0;
	rms = 0.0;
	unresolved = 0.0;
	if (frame < -1 || frame >= frame_count) return;

	int a = clamp(frame, 0, frame_count - 1);
	int b = clamp(frame + 1, 0, frame_count - 1);
	vec4 ea[8];
	vec4 eb[8];
	bands_of(a, ea);
	bands_of(b, eb);

	float total = 0.0;
	for (int i = 0; i < BANDS; i++) {
		float energy = mix(band(ea, i), band(eb, i), frac);
		if (energy <= 0.0) continue;
		total += energy;

		float hz = band_hz(i);
		if (hz < nyquist) {
			// a fixed phase per band, so the wave holds still as the view moves
			float phase = fract(sin(float(i) * 12.9898) * 43758.5453) * TWO_PI;
			value += sqrt(2.0 * energy) * sin(TWO_PI * hz * t + phase);
		} else {
			unresolved += energy;
		}
	}

	rms = sqrt(total);
	unresolved = sqrt(unresolved);
	// the sines add up past the real peak when they align; a soft limit
	// keeps the wave under it without flattening it into a block
	float peak = mix(peak_of(a), peak_of(b), frac);
	value = peak * tanh(value / max(peak, 1e-4));
}

// how much of a pixel's height, centred on y, a span from top to bottom covers
float coverage(float y, float top, float bottom) {
	return clamp(min(y + 0.5, bottom) - max(y - 0.5, top), 0.0, 1.0);
}

void fragment() {
	float half_height = size.y * 0.5;
	float t0 = content_at_left + local.x * pixel_seconds;

	vec4 colour = vec4(0.0);

	if (frame_count > 0) {
		// bands faster than the pixel sampling can follow are noise, not a wave;
		// a reversed clip samples backwards at the same rate
		float sample_seconds = abs(pixel_seconds) / float(SAMPLES);
		float nyquist = 0.5 / sample_seconds;

		float lo = 1e9;
		float hi = -1e9;
		float body = 0.0;
		float noise = 0.0;

		// the moments sampled reach a quarter pixel either side, a box wider
		// than the pixel, so neighbouring columns overlap and edges settle
		for (int i = 0; i < SAMPLES; i++) {
			float t = t0 + ((float(i) + 0.5) / float(SAMPLES) * 1.5 - 0.25) * pixel_seconds;
			float v;
			float r;
			float u;
			synth(t, nyquist, v, r, u);
			lo = min(lo, v);
			hi = max(hi, v);
			body = max(body, r);
			noise = max(noise, u);
		}

		// pixels down from the top; positive values go up
		float top = half_height - (hi + noise) * half_height;
		float bottom = half_height - (lo - noise) * half_height;
		// at least a hairline, so silence still reads as a track
		top = min(top, half_height - 0.5);
		bottom = max(bottom, half_height + 0.5);

		float body_top = half_height - body * half_height;
		float body_bottom = half_height + body * half_height;

		float fill = coverage(local.y, top, bottom);
		float inner = coverage(local.y, body_top, body_bottom);

		colour = mix(peak_color, rms_color, inner);
		colour.a *= fill;
	}

	// the rounded mask
	vec2 half_size = size * 0.5;
	float r = min(radius, min(half_size.x, half_size.y));
	vec2 p = abs(local - half_size) - (half_size - vec2(r));
	float distance_outside = length(max(p, vec2(0.0))) + min(max(p.x, p.y), 0.0) - r;
	colour.a *= 1.0 - smoothstep(-0.75, 0.25, distance_outside);

	COLOR = colour;
}

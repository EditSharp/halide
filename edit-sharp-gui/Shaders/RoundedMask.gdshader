// fades out everything a control draws outside a rounded rectangle the
// size of the control - the thumbnail strip, whose frames would otherwise
// square off the panel's corners. size and radius are set by the control
shader_type canvas_item;

uniform vec2 size = vec2(100.0, 50.0);
uniform float radius = 2.0;

varying vec2 local;

void vertex() {
	local = VERTEX;
}

void fragment() {
	vec2 half_size = size * 0.5;
	float r = min(radius, min(half_size.x, half_size.y));
	vec2 p = abs(local - half_size) - (half_size - vec2(r));
	float distance_outside = length(max(p, vec2(0.0))) + min(max(p.x, p.y), 0.0) - r;
	COLOR.a *= 1.0 - smoothstep(-0.75, 0.25, distance_outside);
}

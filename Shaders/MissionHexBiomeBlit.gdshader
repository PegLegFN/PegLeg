shader_type texture_blit;
render_mode blend_mix;

uniform float intensity : hint_range(0.0, 1.0) = 1.0;
uniform sampler2D BlitSource : hint_blit_source0;

void blit() {
	COLOR0 = MODULATE;
	COLOR0.a = texture(BlitSource, UV).r;
	COLOR0.a -= 1.0-intensity;
	COLOR0.a /= intensity;
}
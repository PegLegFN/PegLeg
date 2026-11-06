shader_type texture_blit;
render_mode blend_mul;

uniform sampler2D BlitSource : hint_blit_source0;

void blit() {
	COLOR0.rgb = vec3(1);
	float val = texture(BlitSource, UV).r;
	COLOR0.a = 1.0 - val;
	//COLOR0.a = val<0.98 ?(1.0 - val) : 1.0;
}
shader_type canvas_item;

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform sampler2D noise : repeat_enable;
uniform float noiseScale = 100.0;
uniform float offsetScale = 15.0;

void fragment() {
	vec2 noiseUV = (UV*ControlSize)/noiseScale;
	float offsetX = texture(noise, noiseUV).r-0.5;
	float offsetY = texture(noise, noiseUV+vec2(300)).r-0.5;
	COLOR.rgb = texture(TEXTURE, UV+vec2(offsetX,offsetY)*(offsetScale/ControlSize)).rgb;
	COLOR.a = 1.0;
}

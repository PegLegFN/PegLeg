shader_type canvas_item;

uniform float progress : hint_range(0.0, 1.0) = 1.0;
uniform float progressMin : hint_range(-0.5, 1.0) = 0.0;
uniform float progressMax : hint_range(0.0, 1.5) = 0.97;
uniform float amp = 0.02;
uniform float speed = 0.1;
uniform float freq = 5.0;
uniform sampler2D bubbles;
uniform float bubbleScale = 1.0;
uniform float bubbleSpeed = 1.0;

varying vec4 modulate;

void vertex()
{
	modulate = COLOR;
}

float waveMask(vec2 uv, float _amp, float _progress, float _speed, float _freq)
{
	return clamp(((uv.y-1.0)+(_progress*(1.0+(_amp*2.5))) + ((sin((uv.x-mod(TIME*_speed, 1.0))*PI*2.0*_freq)-1.0)*_amp))*100.0, 0, 1);
}

void fragment() {
	float _progress = mix(progressMin, progressMax, progress);
	float val = waveMask(UV, amp*0.6, min(_progress*1.05,_progress+0.02), -speed*0.75, freq*1.4)*0.5;
	val += waveMask(UV, amp, _progress, speed, freq);
	
	vec4 patternA = texture(bubbles, vec2(mod((UV.x*bubbleScale)+(sin(UV.y*50.0)*0.02), 1.0), mod((UV.y*bubbleScale)+(TIME*bubbleSpeed*0.06), 1.0)));
	vec4 patternB = texture(bubbles, vec2(mod((UV.x*bubbleScale)+0.25+(sin(UV.y*50.0)*0.01), 1.0), mod((UV.y*bubbleScale)+(TIME*bubbleSpeed*0.12), 1.0)));
	vec4 patternC = texture(bubbles, vec2(mod((UV.x*bubbleScale)+0.7+(sin(UV.y*50.0)*0.015),1.0), mod((UV.y*bubbleScale)+(TIME*bubbleSpeed*0.18), 1.0)));
	vec4 pattern = max(patternA, max(patternB, patternC));
	
	float patternMix = (pattern.a*val)+(1.0-val);
	COLOR.rgb = mix(modulate.rgb, pattern.rgb, pattern.a)*min(val*1.5, 1.0);
	COLOR.a *= clamp((val-0.1)*10.0, 0, 1);
}

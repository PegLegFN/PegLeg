shader_type canvas_item;
#include "Includes/CornerMask.gdshaderinc"

uniform vec2 ControlSize = vec2(100.0, 100.0);
uniform vec2 TargetSize = vec2(90.0, 90.0);
uniform float LineSize = 3.0;
uniform float ProgressFade = 0.02;
uniform float Progress = 0;
uniform bool Circle = false;
uniform bool VerticalHold = false;

float remap(float fromMin, float fromMax, float toMin, float toMax, float value)
{
		float __input_range = fromMax - fromMin;
		float __output_range = toMax - toMin;
		return toMin + __output_range * ((value - fromMin) / __input_range);
}

varying vec4 modulate;

void vertex() {
	modulate = COLOR;
}

void fragment() {
	vec2 sizeDiff = ControlSize-max(TargetSize, vec2(0,0));
	float radius = max(5,max(sizeDiff.x, sizeDiff.y)/2.0);
	//vec2 shrunkSize = ControlSize - vec2(radius);

	float dist = cornerGradient(UV, radius, ControlSize);

	float finalLineSize = max(min(LineSize, radius),0.0001);
	float lineScale = 0.5-((finalLineSize*0.5)/radius);
	dist = remap(lineScale,0.5,0.0,1.0,dist);
	float outline = dist;

	if(outline>0.5)
	{
		outline = 1.0-outline;
	}

	outline = clamp(outline*(finalLineSize),0, 1);

	float fillGradient = VerticalHold ? (1.0-UV.y) : UV.x;
	if(Circle)
		fillGradient = distance(UV,vec2(0.5))*2.1;

	float inverseProgressFase = 1.0/ProgressFade;
	float progressAdditive = (Progress*(1.0+ProgressFade))-ProgressFade;
	float fill = (1.0-(fillGradient*inverseProgressFase))+(progressAdditive*inverseProgressFase);
	fill *= 1.0-clamp((dist*10.0)-5.0,0,1);
	fill = clamp(fill, 0, 1)*0.5 * COLOR.a;

	COLOR.a *= max(outline+fill, 0);
}

//void light() {
	// Called for every pixel for every light affecting the CanvasItem.
	// Uncomment to replace the default light processing function with this one.
//}

shader_type canvas_item;
uniform float time;

float createMask(float gradient, float offset)
{
	return clamp(floor(gradient+offset)*(1.0-floor(gradient+offset-0.1)), 0, 1);
}

void fragment() {
	float gradient=(max(max(UV.x, 1.0-UV.x),max(UV.y, 1.0-UV.y))*2.0)-1.0;
	float offset=mod(TIME, 1);
	float mask = 0.0;
	mask+=createMask(gradient, offset-0.6666);
	mask+=createMask(gradient, offset-0.3333);
	mask+=createMask(gradient, offset);
	mask+=createMask(gradient, offset+0.3333);
	mask+=createMask(gradient, offset+0.6666);
	COLOR.a*=0.25+(mask*pow(gradient, 5)*0.5);
}

//void light() {
	// Called for every pixel for every light affecting the CanvasItem.
	// Uncomment to replace the default light processing function with this one.
//}

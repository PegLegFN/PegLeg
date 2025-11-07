shader_type canvas_item;

uniform bool enabled=false;
uniform float offset:hint_range(0,1)=0;
uniform float scale:hint_range(0,1)=0.25;
uniform float period=1.0;

void fragment() {
	if(enabled)
		COLOR.rgb *= 1.0-mod((TIME-offset)*scale*(1.0/period),scale);
}

//void light() {
//	// Called for every pixel for every light affecting the material.
//	// Uncomment to replace the default light processing function with this one.
//}

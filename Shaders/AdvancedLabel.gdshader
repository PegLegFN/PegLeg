shader_type canvas_item;
uniform vec2 ControlSize;
uniform float PixelsPerLine = 100;
uniform bool debug = false;

uniform vec4 fillColor : source_color = vec4(1);
uniform vec4 shadowColor : source_color = vec4(0,0,0,0.5);
uniform vec4 outlineColor : source_color = vec4(0,0,0,1);

uniform vec3 gradientMask = vec3(1,0,0);
uniform vec4 gradientFrom : source_color;
uniform vec4 gradientTo : source_color = vec4(1);
uniform float gradientOffset;
uniform bool gradientHorizontal;

varying vec2 vertex_pos;
void vertex() {
	vertex_pos = VERTEX;
	vertex_pos.y = mod(vertex_pos.y, PixelsPerLine);
}

void fragment() {
	vec2 realUV = vertex_pos/ControlSize;
	vec3 basisColor = COLOR.rgb;
	float alphaMask = COLOR.a;
	if(basisColor.r>0.0)
	{
		COLOR = fillColor;
	}
	else if(basisColor.g>0.0)
	{
		COLOR = shadowColor;
	}
	else if(basisColor.b>0.0)
	{
		COLOR = outlineColor;
	}
	if(distance(basisColor.rgb * gradientMask, vec3(0)) > 0.0)
	{
		float gradient = gradientHorizontal ? realUV.x : realUV.y;
		gradient+=gradientOffset;
		COLOR *= mix(gradientFrom, gradientTo, gradient);
		if(debug)
		COLOR.rgb = vec3(gradient);
	}
	//COLOR.rgb = vec3(realUV.y+gradientOffset);
	//COLOR.b=0.0;
	COLOR.a *= alphaMask;
}

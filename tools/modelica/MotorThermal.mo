model MotorThermal
  parameter Real thermalMassJPerK = 15 "Lumped motor thermal mass";
  parameter Real coolingWPerK = 0.35 "Cooling conductance to ambient";
  parameter Real ambientC = 25 "Ambient temperature";
  input Real lossW "Copper/iron loss input";
  output Real temperatureC(start = 25) "Motor temperature";
equation
  der(temperatureC) = (lossW - coolingWPerK * (temperatureC - ambientC)) / thermalMassJPerK;
end MotorThermal;

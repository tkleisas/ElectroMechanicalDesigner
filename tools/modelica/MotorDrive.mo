model MotorDrive
  parameter Real kt = 0.1 "Torque constant";
  parameter Real ke = 0.1 "Back-EMF constant";
  parameter Real R = 1 "Winding resistance";
  parameter Real L = 0.001 "Winding inductance";
  parameter Real J = 0.00005 "Rotor inertia";
  parameter Real b = 0.00001 "Viscous damping";
  input Real voltage "Applied voltage";
  output Real torque "Electromagnetic torque";
  output Real speed "Rotor speed";
  Real current(start = 0) "Winding current";
equation
  L * der(current) = voltage - R * current - ke * speed;
  J * der(speed) = kt * current - b * speed;
  torque = kt * current;
end MotorDrive;

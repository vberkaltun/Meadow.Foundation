﻿using System;
using System.Linq;
using Meadow.Hardware;

namespace Meadow.Foundation.Motors.Stepper
{
    /// <summary>
    /// This class is for the A4988 Stepper Motor Driver
    /// </summary>
    public class A4988
    {
        private IDigitalOutputPort _stepPort;
        private IDigitalOutputPort _directionPort;
        private IDigitalOutputPort _enable;
        private IDigitalOutputPort _ms1;
        private IDigitalOutputPort _ms2;
        private IDigitalOutputPort _ms3;
        private StepDivisor _divisor;
        private object _syncRoot = new object();
        private float _stepAngle;
        private int _stepDivisor;

        /// <summary>
        /// Sets or gets the direction of rotation used for Step or Rotate methods.
        /// </summary>
        public RotationDirection Direction { get; set; }

        /// <summary>
        /// Creates an instance of the A4988 Stepper Motor Driver
        /// </summary>
        /// <param name="device">The IIoDevice instance that can create Digital Output Ports</param>
        /// <param name="step">The Meadow pin connected to the STEP pin of the A4988</param>
        /// <param name="direction">The Meadow pin connected to the DIR pin of the A4988</param>
        /// <remarks>You must provide either all of the micro-step (MS) lines or none of them</remarks>
        public A4988(IIODevice device, IPin step, IPin direction)
            : this(device, step, direction, null, null, null, null)
        {
        }

        /// <summary>
        /// Creates an instance of the A4988 Stepper Motor Driver
        /// </summary>
        /// <param name="device">The IIoDevice instance that can create Digital Output Ports</param>
        /// <param name="step">The Meadow pin connected to the STEP pin of the A4988</param>
        /// <param name="direction">The Meadow pin connected to the DIR pin of the A4988</param>
        /// <param name="ms1">The (optional) Meadow pin connected to the MS1 pin of the A4988</param>
        /// <param name="ms2">The (optional) Meadow pin connected to the MS2 pin of the A4988</param>
        /// <param name="ms3">The (optional) Meadow pin connected to the MS3 pin of the A4988</param>
        /// <remarks>You must provide either all of the micro-step (MS) lines or none of them</remarks>
        public A4988(IIODevice device, IPin step, IPin direction, IPin ms1, IPin ms2, IPin ms3)
            : this(device, step, direction, null, ms1, ms2, ms3)
        {

        }

        /// <summary>
        /// Creates an instance of the A4988 Stepper Motor Driver
        /// </summary>
        /// <param name="device">The IIoDevice instance that can create Digital Output Ports</param>
        /// <param name="step">The Meadow pin connected to the STEP pin of the A4988</param>
        /// <param name="direction">The Meadow pin connected to the DIR pin of the A4988</param>
        /// <param name="enable">The (optional) Meadow pin connected to the ENABLE pin of the A4988</param>
        public A4988(IIODevice device, IPin step, IPin direction, IPin enable)
            : this(device, step, direction, enable, null, null, null)
        {

        }

        /// <summary>
        /// Creates an instance of the A4988 Stepper Motor Driver
        /// </summary>
        /// <param name="device">The IIoDevice instance that can create Digital Output Ports</param>
        /// <param name="step">The Meadow pin connected to the STEP pin of the A4988</param>
        /// <param name="direction">The Meadow pin connected to the DIR pin of the A4988</param>
        /// <param name="enable">The (optional) Meadow pin connected to the ENABLE pin of the A4988</param>
        /// <param name="ms1">The (optional) Meadow pin connected to the MS1 pin of the A4988</param>
        /// <param name="ms2">The (optional) Meadow pin connected to the MS2 pin of the A4988</param>
        /// <param name="ms3">The (optional) Meadow pin connected to the MS3 pin of the A4988</param>
        /// <remarks>You must provide either all of the micro-step (MS) lines or none of them</remarks>
        public A4988(IIODevice device, IPin step, IPin direction, IPin enable, IPin ms1, IPin ms2, IPin ms3)
        {
            _stepPort = device.CreateDigitalOutputPort(step);
            _directionPort = device.CreateDigitalOutputPort(direction);
            if (enable != null)
            {
                _enable = device.CreateDigitalOutputPort(enable);
            }

            // micro-step lines (for now) are all-or-nothing
            // TODO: rethink this?
            if (new IPin[] { ms1, ms2, ms3 }.All(p => p != null))
            {
                _ms1 = device.CreateDigitalOutputPort(ms1);
                _ms2 = device.CreateDigitalOutputPort(ms2);
                _ms3 = device.CreateDigitalOutputPort(ms3);
            }
            else if (new IPin[] { ms1, ms2, ms3 }.All(p => p == null))
            {
                // nop
            }
            else
            {
                throw new ArgumentException("All micro-step pins must be either null or valid pins");
            }

            StepAngle = 1.8f; // common default
            RotationSpeedDivisor = 2;
        }

        /// <summary>
        /// Gets the number of steps/micro-steps in the current configuration required for one 360-degree revolution.
        /// </summary>
        public int StepsPerRevolution
        {
            get
            {
                var v = (int)(360f / _stepAngle) * (int)StepDivisor;
                return v;
            }
        }

        /// <summary>
        /// Gets or sets the angle, in degrees, of one step for the connected stepper motor.
        /// </summary>
        public float StepAngle
        {
            get => _stepAngle;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException("Step angle must be positive");
                if (value == _stepAngle) return;
                _stepAngle = value;
            }
        }

        /// <summary>
        /// Divisor for micro-stepping a motor.  This requires the three micro-step control lines to be connected to the motor.
        /// </summary>
        public StepDivisor StepDivisor
        {
            get => _divisor;
            set
            {
                // micro-steps are either all available or not available, so only check one
                // TODO: should we allow partial (i.e. the user uses full or half steps)?
                if ((_ms1 == null) && (value != StepDivisor.Divisor_1))
                {
                    throw new ArgumentException("No Micro Step Pins were provided");
                }

                lock (_syncRoot)
                {
                    switch (value)
                    {
                        case StepDivisor.Divisor_2:
                            _ms1.State = true;
                            _ms2.State = _ms3.State = false;
                            break;
                        case StepDivisor.Divisor_4:
                            _ms2.State = true;
                            _ms1.State = _ms3.State = false;
                            break;
                        case StepDivisor.Divisor_8:
                            _ms1.State = _ms2.State = true;
                            _ms3.State = false;
                            break;
                        case StepDivisor.Divisor_16:
                            _ms1.State = _ms2.State = _ms3.State = true;
                            break;
                        default:
                            _ms1.State = _ms2.State = _ms3.State = false;
                            break;
                    }

                    _divisor = value;
                }
            }
        }

        /// <summary>
        /// Rotates the stepper motor a specified number of degrees
        /// </summary>
        /// <param name="count">Degrees to rotate</param>
        /// <param name="direction">Direction of rotation</param>
        public void Rotate(float degrees, RotationDirection direction)
        {
            Direction = direction;
            Rotate(degrees);
        }

        /// <summary>
        /// Rotates the stepper motor a specified number of degrees
        /// </summary>
        /// <param name="count">Degrees to rotate</param>
        public void Rotate(float degrees)
        {
            // how many steps is it?
            var stepsRequired = (int)(StepsPerRevolution / 360f * degrees);
            Step(stepsRequired);
        }

        /// <summary>
        /// Divisor used to adjust rotaional speed of the stepper motor
        /// </summary>
        public int RotationSpeedDivisor
        {
            get => _stepDivisor;
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException("Divisor must be >= 1");
                if (value == RotationSpeedDivisor) return;
                _stepDivisor = value;
            }

        }

        /// <summary>
        /// Rotates the stepper motor a specified number of steps (or microsteps)
        /// </summary>
        /// <param name="count">Number of steps to rotate</param>
        /// <param name="direction">Direction of rotation</param>
        public void Step(int count, RotationDirection direction)
        {
            Direction = direction;
            Step(count);
        }

        /// <summary>
        /// Rotates the stepper motor a specified number of steps (or microsteps)
        /// </summary>
        /// <param name="count">Number of steps to rotate</param>
        public void Step(int count)
        {
            lock (_syncRoot)
            {
                _directionPort.State = Direction == RotationDirection.Clockwise;

                // TODO: add acceleration
                for (int i = 0; i < count; i++)
                {
                    // HACK HACK HACK
                    // We know that each call to set state true == ~210us on Beta 3.10
                    // We could use unmanaged code to tune it better, but we need a <1ms sleep to do it
                    for (var s = 0; s < RotationSpeedDivisor; s++)
                    {
                        _stepPort.State = true;
                    }

                    _stepPort.State = false;
                }
                // TODO: add deceleration
            }
        }
    }
}
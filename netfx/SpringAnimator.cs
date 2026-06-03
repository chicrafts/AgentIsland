using System;
using System.Windows.Media;

namespace AgentIsland
{
    public sealed class SpringAnimator
    {
        private readonly double stiffness;
        private readonly double damping;
        private readonly double mass;
        private readonly double settleThreshold;
        private TimeSpan lastFrame;
        private TimeSpan lastEmitFrame;
        private bool running;
        private static readonly TimeSpan EmitInterval = TimeSpan.FromMilliseconds(13);

        public event Action<double> ValueChanged;
        public event Action Completed;

        public SpringAnimator(double initialValue, double stiffness, double damping, double mass, double settleThreshold)
        {
            Position = initialValue;
            Target = initialValue;
            this.stiffness = stiffness;
            this.damping = damping;
            this.mass = mass <= 0 ? 1 : mass;
            this.settleThreshold = settleThreshold;
        }

        public double Position { get; private set; }
        public double Velocity { get; private set; }
        public double Target { get; private set; }
        public bool IsRunning { get { return running; } }

        public void JumpTo(double value)
        {
            Stop();
            Position = value;
            Target = value;
            Velocity = 0;
            RaiseValueChanged();
        }

        public void SetTarget(double target)
        {
            var alreadySettled = Math.Abs(Target - target) < 0.001 &&
                                 Math.Abs(Position - target) < settleThreshold &&
                                 Math.Abs(Velocity) < settleThreshold * 10;
            if (alreadySettled)
            {
                Position = target;
                Target = target;
                Velocity = 0;
                RaiseValueChanged();
                return;
            }

            if (Math.Abs(Target - target) > 0.001)
            {
                var movingToward = (target - Position) * Velocity >= 0;
                Velocity *= movingToward ? 0.78 : 0.45;
            }

            Target = target;
            if (running)
            {
                return;
            }

            running = true;
            lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }

        public void Stop()
        {
            if (!running)
            {
                return;
            }

            CompositionTarget.Rendering -= OnRendering;
            running = false;
            lastFrame = TimeSpan.Zero;
            lastEmitFrame = TimeSpan.Zero;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            var args = e as RenderingEventArgs;
            if (args == null)
            {
                return;
            }

            double dt;
            if (lastFrame == TimeSpan.Zero)
            {
                lastFrame = args.RenderingTime;
                dt = 1.0 / 90.0;
            }
            else
            {
                dt = (args.RenderingTime - lastFrame).TotalSeconds;
                lastFrame = args.RenderingTime;
                if (dt <= 0)
                {
                    return;
                }
            }

            if (dt > 1.0 / 30.0)
            {
                dt = 1.0 / 30.0;
            }

            Step(dt);

            if (Math.Abs(Position - Target) < settleThreshold && Math.Abs(Velocity) < settleThreshold * 10)
            {
                Position = Target;
                Velocity = 0;
                RaiseValueChanged();
                Stop();
                if (Completed != null)
                {
                    Completed();
                }
                return;
            }

            var sinceEmit = args.RenderingTime - lastEmitFrame;
            if (lastEmitFrame == TimeSpan.Zero || sinceEmit >= EmitInterval)
            {
                lastEmitFrame = args.RenderingTime;
                RaiseValueChanged();
            }
        }

        private void Step(double dt)
        {
            var remaining = dt;
            const double maxStep = 1.0 / 120.0;
            while (remaining > 0)
            {
                var step = remaining > maxStep ? maxStep : remaining;
                var displacement = Position - Target;
                var acceleration = (-stiffness * displacement - damping * Velocity) / mass;
                Velocity += acceleration * step;
                Position += Velocity * step;
                remaining -= step;
            }
        }

        private void RaiseValueChanged()
        {
            if (ValueChanged != null)
            {
                ValueChanged(Position);
            }
        }
    }
}

﻿namespace SS3D.Interactions
{
    /// <summary>
    /// A reference to a currently running interaction
    /// </summary>
    public class InteractionReference
    {
        /// <summary>
        /// The id of this instance
        /// </summary>
        public int Id { get; }

        public InteractionReference(int id)
        {
            Id = id;
        }

        protected bool Equals(InteractionReference other)
        {
            return Id == other.Id;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return obj.GetType() == GetType() && Equals((InteractionReference) obj);
        }

        public override int GetHashCode()
        {
            return Id;
        }
        
    }
}
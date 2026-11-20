using System;
using System.Collections.Concurrent;


namespace Bivrost
{

	/// <summary>
	/// A threadsafe action queue - given that Run*Action methods are run from one thread
	/// </summary>
	public class ActionQueue
	{
		protected ConcurrentQueue<Action> actions = new ConcurrentQueue<Action>();


		public void Enqueue(Action a)
		{
			if (a == null)
				throw new ArgumentNullException(nameof(a));
			actions.Enqueue(a);
		}


		public void RunOneAction()
		{
			Action action;
			if (actions.TryDequeue(out action))
				action();
		}


		public void RunAllActions()
		{
			int count = actions.Count;
			Action action;
			for (int i = 0; i < count && actions.TryDequeue(out action); i++)
				action();
		}


		public void Clear()
		{
			Action action;
			while (actions.TryDequeue(out action))
				;
		}



	}
}

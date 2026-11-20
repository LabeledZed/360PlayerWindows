using Caliburn.Micro;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bivrost.Bivrost360Player.ConfigUI
{
	public class EnumConfigItemViewModel : ConfigItemBase<int>
	{
		private Type _enumType;
		private Dictionary<string, int> enumMap;

		public EnumConfigItemViewModel(SettingsPropertyAttribute attr, Func<int> loadCallback, Action<int> saveCallback, Type enumType) : base(attr, loadCallback, saveCallback) {
			_enumType = enumType;
			var values = Enum.GetValues(enumType);
            EnumList = new List<string>();
			enumMap = new Dictionary<string, int>();
			foreach (object v in values)
			{
				string desc = GetDescription(v);
				if (!enumMap.ContainsKey(desc))
				{
					enumMap.Add(desc, (int)v);
					EnumList.Add(desc);
				}
			}
		}

		string GetDescription(object enumObject)
		{
			var type = enumObject.GetType();
			var memberInfo = type.GetMember(enumObject.ToString());
			if (memberInfo.Length > 0)
			{
				var attributes = memberInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);
				if (attributes.Length > 0)
				{
					var attr = attributes.First() as DescriptionAttribute;
					if (attr != null)
						return attr.Description;
				}
			}
			return enumObject.ToString();
		}

		public List<string> EnumList { get; set; }

		public new string Value
		{
			get
			{
				var match = enumMap.FirstOrDefault(e => e.Value == base.Value);
				return match.Key ?? EnumList.FirstOrDefault();
			}

			set
			{
				if (value != null && enumMap.TryGetValue(value, out int enumVal))
				{
					base.Value = enumVal;
					NotifyOfPropertyChange(() => Value);
				}
            }
		}

	}
}

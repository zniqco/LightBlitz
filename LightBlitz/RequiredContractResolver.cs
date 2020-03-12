using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;

namespace LightBlitz
{
    public class RequiredContractResolver : DefaultContractResolver
    {
        public static readonly RequiredContractResolver Instance = new RequiredContractResolver();

        protected override JsonContract CreateContract(Type objectType)
        {
            var contract = base.CreateContract(objectType);
            var objectContract = contract as JsonObjectContract;

            if (objectContract != null)
                objectContract.ItemRequired = Required.Always;

            return contract;
        }
    }
}

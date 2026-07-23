using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

namespace Logistics {
    /// <summary>
    /// 世界级储存库表：Guid → <see cref="StorageVault"/>。
    /// 帧末冲刷挂起的 Compact，使宿主拖拽 Remove+Add 整笔完成后再压实。
    /// </summary>
    public class SubsystemStorageVaults : Subsystem, IUpdateable {
        public const string SaveKeyVaults = "Vaults";

        readonly Dictionary<Guid, StorageVault> m_vaults = new();

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public bool TryGet(Guid id, out StorageVault vault) => m_vaults.TryGetValue(id, out vault);

        public StorageVault Get(Guid id) {
            if (!m_vaults.TryGetValue(id, out StorageVault vault)) {
                throw new InvalidOperationException($"Storage vault {id} not found.");
            }
            return vault;
        }

        public StorageVault Create(Guid id, int memberCount) {
            if (m_vaults.ContainsKey(id)) {
                throw new InvalidOperationException($"Storage vault {id} already exists.");
            }
            var vault = new StorageVault(id, memberCount);
            m_vaults[id] = vault;
            return vault;
        }

        public bool Remove(Guid id) => m_vaults.Remove(id);

        public void Update(float dt) {
            foreach (StorageVault vault in m_vaults.Values) {
                vault.CompactIfNeeded();
            }
        }

        public override void Load(ValuesDictionary valuesDictionary) {
            base.Load(valuesDictionary);
            m_vaults.Clear();
            ValuesDictionary vaultsVd = valuesDictionary.GetValue<ValuesDictionary>(SaveKeyVaults, null);
            if (vaultsVd == null) return;
            foreach (KeyValuePair<string, object> kv in vaultsVd) {
                if (kv.Value is not ValuesDictionary vaultVd) continue;
                StorageVault vault = StorageVault.Read(vaultVd);
                m_vaults[vault.Id] = vault;
            }
        }

        public override void Save(ValuesDictionary valuesDictionary) {
            base.Save(valuesDictionary);
            ValuesDictionary vaultsVd = new();
            foreach (KeyValuePair<Guid, StorageVault> kv in m_vaults) {
                ValuesDictionary vaultVd = new();
                kv.Value.Write(vaultVd);
                vaultsVd.SetValue(kv.Key.ToString("D"), vaultVd);
            }
            valuesDictionary.SetValue(SaveKeyVaults, vaultsVd);
        }
    }
}

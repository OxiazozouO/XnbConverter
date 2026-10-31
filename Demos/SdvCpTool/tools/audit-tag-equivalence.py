"""某个 context tag 是不是**恰好等价于**某个分类号？—— 从游戏自己的数据推，不猜。

判据：
  · 带这个 tag 的原版物件，`Category` 是否**只有一个值** C（tag ⊆ C）；
  · 以及 `Category == C` 的原版物件是否**全都带**这个 tag（C ⊆ tag）。
两条都成立 ⇒ 两者在**原版物件集合上等价**，可以把 tag 写成 `-C`。

⚠ 只在**原版数据**上等价 —— 别的 mod 新加的物件可能只有 tag 没有那个 Category。
所以结论要带上"基于原版 N 个物件"这句限定。
"""
import collections
import json
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

d = json.load(open(r'D:\sdv_tree\Data\Objects.json', encoding='utf-8-sig'))

by_tag = collections.defaultdict(set)
by_cat = collections.defaultdict(set)
for key, v in d.items():
    if not isinstance(v, dict):
        continue
    cat = v.get('Category')
    if isinstance(cat, int):
        by_cat[cat].add(key)
    for t in (v.get('ContextTags') or []):
        by_tag[t].add(key)

# 分类号 → 常量名（来自 DLL 反编译，姑且只列几个常见的）
NAMES = {-2: 'Gem', -4: 'Fish', -5: 'Egg', -6: 'Milk', -7: 'Cooking', -8: 'Crafting',
         -9: 'BigCraftable', -12: 'Minerals', -14: 'Meat', -15: 'Metal', -16: 'Building',
         -19: 'Fertilizer', -20: 'Junk', -21: 'Bait', -22: 'Tackle', -24: 'Furniture',
         -25: 'Ingredients', -26: 'ArtisanGoods', -27: 'Syrup', -28: 'MonsterLoot',
         -74: 'Seeds', -75: 'Vegetable', -79: 'Fruit', -80: 'Flower', -81: 'Greens',
         -95: 'Hat', -96: 'Ring', -97: 'Boots', -98: 'Weapon', -99: 'Tool'}

print(f'原版 Data/Objects {len(d)} 条；{len(by_tag)} 种 context tag\n')
for tag in ('egg_item', 'milk_item', 'cheese_item', 'vegetable_item', 'fruit_item',
            'fish_item', 'meat_item', 'flower_item', 'seed_item'):
    items = by_tag.get(tag)
    if not items:
        print(f'{tag:16} 原版没有物件带这个 tag')
        continue
    cats = {d[k].get('Category') for k in items}
    if len(cats) != 1:
        print(f'{tag:16} 跨 {len(cats)} 个分类 {sorted(x for x in cats if x is not None)} ⇒ **不等价**')
        continue
    c = cats.pop()
    only_tag = by_cat.get(c, set()) - items
    verdict = '等价' if not only_tag else f'**不等价**（分类 {c} 里还有 {len(only_tag)} 个不带该 tag，如 {sorted(only_tag)[:2]}）'
    print(f'{tag:16} → 分类 {c}（{NAMES.get(c, "?")}）；{len(items)} 个物件，{verdict}')

const API_BASE = '/api/products';

const elements = {
  alert: document.getElementById('alert'),
  loading: document.getElementById('loading'),
  emptyState: document.getElementById('empty-state'),
  tableWrap: document.getElementById('table-wrap'),
  productsBody: document.getElementById('products-body'),
  productDialog: document.getElementById('product-dialog'),
  deleteDialog: document.getElementById('delete-dialog'),
  productForm: document.getElementById('product-form'),
  dialogTitle: document.getElementById('dialog-title'),
  productId: document.getElementById('product-id'),
  productName: document.getElementById('product-name'),
  productPrice: document.getElementById('product-price'),
  productQuantity: document.getElementById('product-quantity'),
  deleteMessage: document.getElementById('delete-message'),
};

let deleteTargetId = null;

function formatCurrency(value) {
  return new Intl.NumberFormat('pt-BR', {
    style: 'currency',
    currency: 'BRL',
  }).format(value);
}

function showAlert(message, type = 'error') {
  elements.alert.textContent = message;
  elements.alert.className = `alert alert--${type}`;
  elements.alert.hidden = false;
  clearTimeout(showAlert._timer);
  showAlert._timer = setTimeout(() => {
    elements.alert.hidden = true;
  }, 5000);
}

async function apiRequest(url, options = {}) {
  const response = await fetch(url, {
    headers: { 'Content-Type': 'application/json', ...options.headers },
    ...options,
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `Erro ${response.status}`);
  }

  if (response.status === 204) {
    return null;
  }

  const contentType = response.headers.get('content-type') || '';
  if (contentType.includes('application/json')) {
    return response.json();
  }

  return null;
}

function setLoading(isLoading) {
  elements.loading.hidden = !isLoading;
}

function renderProducts(products) {
  const hasProducts = products.length > 0;
  elements.emptyState.hidden = hasProducts;
  elements.tableWrap.hidden = !hasProducts;

  elements.productsBody.innerHTML = products
    .map(
      (p) => `
      <tr>
        <td>${p.id}</td>
        <td>${escapeHtml(p.name)}</td>
        <td>${formatCurrency(p.price)}</td>
        <td>${p.quantity}</td>
        <td>
          <div class="actions">
            <button type="button" class="btn btn--ghost btn--sm" data-action="edit" data-id="${p.id}">Editar</button>
            <button type="button" class="btn btn--danger btn--sm" data-action="delete" data-id="${p.id}">Excluir</button>
          </div>
        </td>
      </tr>`
    )
    .join('');
}

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

async function loadProducts() {
  setLoading(true);
  elements.alert.hidden = true;

  try {
    const products = await apiRequest(API_BASE);
    renderProducts(products);
  } catch (err) {
    showAlert(`Falha ao carregar produtos: ${err.message}`);
    elements.emptyState.hidden = true;
    elements.tableWrap.hidden = true;
  } finally {
    setLoading(false);
  }
}

function openCreateDialog() {
  elements.dialogTitle.textContent = 'Novo produto';
  elements.productForm.reset();
  elements.productId.value = '';
  elements.productDialog.showModal();
  elements.productName.focus();
}

async function openEditDialog(id) {
  try {
    const product = await apiRequest(`${API_BASE}/${id}`);
    elements.dialogTitle.textContent = 'Editar produto';
    elements.productId.value = product.id;
    elements.productName.value = product.name;
    elements.productPrice.value = product.price;
    elements.productQuantity.value = product.quantity;
    elements.productDialog.showModal();
    elements.productName.focus();
  } catch (err) {
    showAlert(`Falha ao carregar produto: ${err.message}`);
  }
}

function openDeleteDialog(id) {
  const row = elements.productsBody.querySelector(`[data-action="delete"][data-id="${id}"]`)?.closest('tr');
  const name = row?.children[1]?.textContent || 'este produto';
  deleteTargetId = id;
  elements.deleteMessage.textContent = `Tem certeza que deseja excluir "${name}"?`;
  elements.deleteDialog.showModal();
}

async function saveProduct(event) {
  event.preventDefault();

  const payload = {
    name: elements.productName.value.trim(),
    price: parseFloat(elements.productPrice.value),
    quantity: parseInt(elements.productQuantity.value, 10),
  };

  const id = elements.productId.value;
  const isEdit = Boolean(id);

  try {
    if (isEdit) {
      await apiRequest(`${API_BASE}/${id}`, {
        method: 'PUT',
        body: JSON.stringify(payload),
      });
      showAlert('Produto atualizado com sucesso.', 'success');
    } else {
      await apiRequest(API_BASE, {
        method: 'POST',
        body: JSON.stringify(payload),
      });
      showAlert('Produto criado com sucesso.', 'success');
    }

    elements.productDialog.close();
    await loadProducts();
  } catch (err) {
    showAlert(`Falha ao salvar: ${err.message}`);
  }
}

async function confirmDelete() {
  if (!deleteTargetId) return;

  try {
    await apiRequest(`${API_BASE}/${deleteTargetId}`, { method: 'DELETE' });
    elements.deleteDialog.close();
    deleteTargetId = null;
    showAlert('Produto excluído com sucesso.', 'success');
    await loadProducts();
  } catch (err) {
    showAlert(`Falha ao excluir: ${err.message}`);
  }
}

document.getElementById('btn-new').addEventListener('click', openCreateDialog);
document.getElementById('btn-empty-new').addEventListener('click', openCreateDialog);
document.getElementById('btn-refresh').addEventListener('click', loadProducts);
document.getElementById('btn-close-dialog').addEventListener('click', () => elements.productDialog.close());
document.getElementById('btn-cancel').addEventListener('click', () => elements.productDialog.close());
document.getElementById('btn-close-delete').addEventListener('click', () => elements.deleteDialog.close());
document.getElementById('btn-cancel-delete').addEventListener('click', () => elements.deleteDialog.close());
document.getElementById('btn-confirm-delete').addEventListener('click', confirmDelete);
elements.productForm.addEventListener('submit', saveProduct);

elements.productsBody.addEventListener('click', (event) => {
  const button = event.target.closest('[data-action]');
  if (!button) return;

  const { action, id } = button.dataset;
  if (action === 'edit') openEditDialog(id);
  if (action === 'delete') openDeleteDialog(id);
});

loadProducts();
